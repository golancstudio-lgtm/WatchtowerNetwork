using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.LinQuick;
using MathF = TaleWorlds.Library.MathF;

namespace WatchtowerNetwork.Heatmaps;

public static class HeatmapBuilder
{
    public const float GridStep = 1f;
    private const int MaxTownPathCandidates = 6;
    private const float TooFarDistanceMultiplier = 1.15f;
    private const float HeatDropExponent = 3f;
    private const int LandSmoothingIterations = 2;
    private const int MinRowsForParallel = 32;

    public static HeatmapData Build(string mapModuleId, string gameVersion)
    {
        if (Campaign.Current?.MapSceneWrapper == null)
        {
            throw new InvalidOperationException("Campaign map scene is not available.");
        }

        Campaign.Current.MapSceneWrapper.GetMapBorders(out Vec2 min, out Vec2 max, out _);
        int width = (int)MathF.Floor((max.X - min.X) / GridStep) + 1;
        int height = (int)MathF.Floor((max.Y - min.Y) / GridStep) + 1;

        if (width <= 0 || height <= 0)
        {
            throw new InvalidOperationException("Invalid map bounds for heatmap generation.");
        }

        LoadingProcessHintTracker.BeginTask("Generating heatmap...", height);

        List<TownPathTarget> townTargets = Town.AllTowns
            .Where(t => t?.Settlement != null && t.Settlement.GatePosition.Face.IsValid())
            .Select(t => new TownPathTarget(t.Name.ToString(), t.Settlement.GatePosition.ToVec2()))
            .ToList();
        float tooFarDistanceCutoff = ResolveTooFarDistanceCutoff();
        int cellCount = checked(width * height);
        HeatmapCell[] cells = new HeatmapCell[cellCount];
        Vec2[] worldPositions = new Vec2[cellCount];
        float[] nearestTownDistances = new float[cellCount];
        float maxDistance = 0f;

        try
        {
            for (int y = 0; y < height; y++)
            {
                float worldY = min.Y + (y * GridStep);
                for (int x = 0; x < width; x++)
                {
                    float worldX = min.X + (x * GridStep);
                    int index = y * width + x;
                    Vec2 worldPosition = new Vec2(worldX, worldY);
                    worldPositions[index] = worldPosition;
                    CampaignVec2 sample = new CampaignVec2(worldPosition, isOnLand: true);

                    bool isLand = IsLand(sample);
                    cells[index] = new HeatmapCell(
                        posX: ToUShortClamped(x),
                        posY: ToUShortClamped(y),
                        isLand: isLand,
                        distance: byte.MinValue);

                    if (!isLand)
                    {
                        nearestTownDistances[index] = -1f;
                        continue;
                    }

                    nearestTownDistances[index] = -1f;
                }

                LoadingProcessHintTracker.ReportProgress(y + 1);
            }
        }
        finally
        {
            LoadingProcessHintTracker.CompleteTask();
        }

        HeatmapHeader header = new HeatmapHeader
        {
            MapModuleId = mapModuleId,
            GameVersion = gameVersion,
            GridWidth = width,
            GridHeight = height,
            GridStep = GridStep,
            MinX = min.X,
            MinY = min.Y,
            MaxX = max.X,
            MaxY = max.Y
        };

        HeatmapAStarDistanceSolver distanceSolver = new HeatmapAStarDistanceSolver(header, cells);
        LoadingProcessHintTracker.BeginTask("Resolving town path distances...", height);
        try
        {
            int completedRows = 0;
            object maxDistanceLock = new object();
            Parallel.For(
                fromInclusive: 0,
                toExclusive: height,
                localInit: () => new RowProcessingContext(new HeatmapAStarDistanceSolver(header, cells)),
                body: (y, _, context) =>
                {
                    for (int x = 0; x < width; x++)
                    {
                        int index = y * width + x;
                        if (!cells[index].IsLand)
                        {
                            continue;
                        }

                        Vec2 worldPosition = worldPositions[index];
                        if (!TryGetNearestPathDistance(
                                worldPosition,
                                townTargets,
                                context.Solver,
                                tooFarDistanceCutoff,
                                out float nearestDistance))
                        {
                            // Land tile with no reachable town path, or too far from all reachable towns.
                            cells[index].IsLand = false;
                            cells[index].Distance = byte.MinValue;
                            nearestTownDistances[index] = -1f;
                            continue;
                        }

                        nearestTownDistances[index] = nearestDistance;
                        if (nearestDistance > context.LocalMaxDistance)
                        {
                            context.LocalMaxDistance = nearestDistance;
                        }
                    }

                    int rowsDone = Interlocked.Increment(ref completedRows);
                    LoadingProcessHintTracker.ReportProgress(rowsDone);
                    return context;
                },
                localFinally: context =>
                {
                    if (context.LocalMaxDistance <= 0f)
                    {
                        return;
                    }

                    lock (maxDistanceLock)
                    {
                        if (context.LocalMaxDistance > maxDistance)
                        {
                            maxDistance = context.LocalMaxDistance;
                        }
                    }
                });
        }
        finally
        {
            LoadingProcessHintTracker.CompleteTask();
        }

        SmoothLandDistances(nearestTownDistances, cells, width, height);

        maxDistance = 0f;
        object finalMaxLock = new object();
        Parallel.For(
            fromInclusive: 0,
            toExclusive: cells.Length,
            localInit: () => 0f,
            body: (i, _, localMax) =>
            {
                if (!cells[i].IsLand)
                {
                    return localMax;
                }

                float distance = nearestTownDistances[i];
                return distance > localMax ? distance : localMax;
            },
            localFinally: localMax =>
            {
                if (localMax <= 0f)
                {
                    return;
                }

                lock (finalMaxLock)
                {
                    if (localMax > maxDistance)
                    {
                        maxDistance = localMax;
                    }
                }
            });

        Parallel.For(0, cells.Length, i =>
        {
            if (!cells[i].IsLand)
            {
                return;
            }

            float distance = nearestTownDistances[i];
            byte scaledDistance = ScaleDistanceToByte(distance, maxDistance);
            cells[i].Distance = scaledDistance;
        });

        return new HeatmapData(header, cells);
    }

    public static Task<HeatmapData> BuildAsync(string mapModuleId, string gameVersion)
    {
        return Task.Run(() => Build(mapModuleId, gameVersion));
    }

    private static bool IsLand(in CampaignVec2 sample)
    {
        TerrainType terrainType = Campaign.Current.MapSceneWrapper.GetTerrainTypeAtPosition(in sample);
        if (!Campaign.Current.Models.PartyNavigationModel
                .IsTerrainTypeValidForNavigationType(terrainType, MobileParty.NavigationType.Default))
        {
            return false;
        }

        bool isReachable = Campaign.Current.Models.PartyNavigationModel
            .CanPlayerNavigateToPosition(sample, out MobileParty.NavigationType navigationType);
        if (!isReachable)
        {
            return false;
        }

        return navigationType == MobileParty.NavigationType.Default ||
               navigationType == MobileParty.NavigationType.All;
    }

    private static bool TryGetNearestPathDistance(
        Vec2 cellPosition,
        List<TownPathTarget> townTargets,
        HeatmapAStarDistanceSolver distanceSolver,
        float tooFarDistanceCutoff,
        out float nearestDistance)
    {
        nearestDistance = float.MaxValue;
        if (townTargets.Count == 0)
        {
            return false;
        }

        int[] candidateIndexes = SelectNearestTownCandidates(cellPosition, townTargets);
        if (candidateIndexes.Length == 0)
        {
            return false;
        }

        bool foundPath = false;
        float currentDistanceLimit = tooFarDistanceCutoff;
        for (int i = 0; i < candidateIndexes.Length; i++)
        {
            TownPathTarget target = townTargets[candidateIndexes[i]];
            if (!distanceSolver.TryGetPathDistance(
                    cellPosition,
                    target.WorldPosition,
                    currentDistanceLimit,
                    out float distance))
            {
                continue;
            }

            foundPath = true;
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                currentDistanceLimit = distance;
            }
        }

        return foundPath && nearestDistance <= tooFarDistanceCutoff;
    }

    private static int[] SelectNearestTownCandidates(Vec2 cellWorldPosition, List<TownPathTarget> townTargets)
    {
        int count = townTargets.Count;
        if (count == 0)
        {
            return Array.Empty<int>();
        }

        int candidateCount = Math.Min(MaxTownPathCandidates, count);
        float[] bestDistanceSquared = new float[candidateCount];
        int[] bestIndexes = new int[candidateCount];
        for (int i = 0; i < candidateCount; i++)
        {
            bestDistanceSquared[i] = float.MaxValue;
            bestIndexes[i] = -1;
        }

        for (int i = 0; i < count; i++)
        {
            float distanceSquared = cellWorldPosition.DistanceSquared(townTargets[i].WorldPosition);
            if (distanceSquared >= bestDistanceSquared[candidateCount - 1])
            {
                continue;
            }

            int insertAt = candidateCount - 1;
            while (insertAt > 0 && distanceSquared < bestDistanceSquared[insertAt - 1])
            {
                bestDistanceSquared[insertAt] = bestDistanceSquared[insertAt - 1];
                bestIndexes[insertAt] = bestIndexes[insertAt - 1];
                insertAt--;
            }

            bestDistanceSquared[insertAt] = distanceSquared;
            bestIndexes[insertAt] = i;
        }

        int validCount = 0;
        while (validCount < bestIndexes.Length && bestIndexes[validCount] != -1)
        {
            validCount++;
        }

        if (validCount == bestIndexes.Length)
        {
            return bestIndexes;
        }

        int[] compact = new int[validCount];
        Array.Copy(bestIndexes, compact, validCount);
        return compact;
    }

    private static float ResolveTooFarDistanceCutoff()
    {
        float modeledMax = Campaign.Current.Models.MapDistanceModel
            .GetMaximumDistanceBetweenTwoConnectedSettlements(MobileParty.NavigationType.Default);
        float baseDistance = modeledMax > 0f ? modeledMax : Campaign.MapDiagonal;
        return baseDistance * TooFarDistanceMultiplier;
    }

    private static void SmoothLandDistances(float[] distances, HeatmapCell[] cells, int width, int height)
    {
        float[] source = distances;
        float[] target = new float[distances.Length];

        for (int iteration = 0; iteration < LandSmoothingIterations; iteration++)
        {
            Array.Copy(source, target, source.Length);
            bool useParallel = height >= MinRowsForParallel;
            if (useParallel)
            {
                Parallel.For(0, height, y =>
                {
                    for (int x = 0; x < width; x++)
                    {
                        int index = y * width + x;
                        if (!cells[index].IsLand)
                        {
                            continue;
                        }

                        float weightedSum = source[index] * 4f;
                        float weightTotal = 4f;

                        for (int oy = -1; oy <= 1; oy++)
                        {
                            int ny = y + oy;
                            if (ny < 0 || ny >= height)
                            {
                                continue;
                            }

                            for (int ox = -1; ox <= 1; ox++)
                            {
                                if (ox == 0 && oy == 0)
                                {
                                    continue;
                                }

                                int nx = x + ox;
                                if (nx < 0 || nx >= width)
                                {
                                    continue;
                                }

                                int neighborIndex = ny * width + nx;
                                if (!cells[neighborIndex].IsLand)
                                {
                                    continue;
                                }

                                float weight = (ox == 0 || oy == 0) ? 2f : 1f;
                                weightedSum += source[neighborIndex] * weight;
                                weightTotal += weight;
                            }
                        }

                        target[index] = weightedSum / weightTotal;
                    }
                });
            }
            else
            {
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int index = y * width + x;
                        if (!cells[index].IsLand)
                        {
                            continue;
                        }

                        float weightedSum = source[index] * 4f;
                        float weightTotal = 4f;

                        for (int oy = -1; oy <= 1; oy++)
                        {
                            int ny = y + oy;
                            if (ny < 0 || ny >= height)
                            {
                                continue;
                            }

                            for (int ox = -1; ox <= 1; ox++)
                            {
                                if (ox == 0 && oy == 0)
                                {
                                    continue;
                                }

                                int nx = x + ox;
                                if (nx < 0 || nx >= width)
                                {
                                    continue;
                                }

                                int neighborIndex = ny * width + nx;
                                if (!cells[neighborIndex].IsLand)
                                {
                                    continue;
                                }

                                float weight = (ox == 0 || oy == 0) ? 2f : 1f;
                                weightedSum += source[neighborIndex] * weight;
                                weightTotal += weight;
                            }
                        }

                        target[index] = weightedSum / weightTotal;
                    }
                }
            }

            if (iteration < LandSmoothingIterations - 1)
            {
                float[] tmp = source;
                source = target;
                target = tmp;
            }
        }

        if (!ReferenceEquals(source, distances))
        {
            Array.Copy(source, distances, distances.Length);
        }
    }

    private static byte ScaleDistanceToByte(float distance, float maxDistance)
    {
        if (maxDistance <= 0f)
        {
            return byte.MaxValue;
        }

        float normalized = distance / maxDistance;
        float inverted = 1f - normalized;
        float curved = (float)Math.Pow(inverted, HeatDropExponent);
        float scaled = MathF.Floor(curved * byte.MaxValue);
        if (scaled < byte.MinValue)
        {
            return byte.MinValue;
        }

        if (scaled > byte.MaxValue)
        {
            return byte.MaxValue;
        }

        return (byte)scaled;
    }

    private static ushort ToUShortClamped(int value)
    {
        if (value <= ushort.MinValue)
        {
            return ushort.MinValue;
        }

        if (value >= ushort.MaxValue)
        {
            return ushort.MaxValue;
        }

        return (ushort)value;
    }

    private readonly struct TownPathTarget
    {
        public string TownName { get; }
        public Vec2 WorldPosition { get; }

        public TownPathTarget(string townName, Vec2 position)
        {
            TownName = townName;
            WorldPosition = position;
        }
    }

    private sealed class RowProcessingContext
    {
        public HeatmapAStarDistanceSolver Solver { get; }
        public float LocalMaxDistance { get; set; }

        public RowProcessingContext(HeatmapAStarDistanceSolver solver)
        {
            Solver = solver;
            LocalMaxDistance = 0f;
        }
    }
}

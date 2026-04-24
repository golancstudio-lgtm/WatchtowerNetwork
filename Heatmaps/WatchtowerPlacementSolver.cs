using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;
using TaleWorlds.ModuleManager;

namespace WatchtowerNetwork.Heatmaps;

public sealed class WatchtowerPlacement
{
    public string WatchtowerId { get; init; } = string.Empty;
    public string WatchtowerName { get; init; } = string.Empty;
    public Vec2 Position { get; init; } = Vec2.Zero;
    public Vec2 GatePosition { get; init; } = Vec2.Zero;
    public string WatchtowerBound { get; init; } = string.Empty;
    public float BackgroundCropPosition { get; init; } = 0f;
    public string BackgroudMesh { get; init; } = string.Empty;
    public string WaitMesh { get; init; } = string.Empty;
}

public sealed class WatchtowerPlacementParameters
{
    public int CandidateStride { get; init; } = 2;
    public float MaximumDistanceFromParentTown { get; init; } = 25f;
    public float MinimumDistanceFromParentTown { get; init; } = 8f;
    public float MinimumDistanceBetweenWatchtowers { get; init; } = 20f;
    public int MaximumThreatOriginsPerTown { get; init; } = 20;
    public float InterceptDetourTolerance { get; init; } = 8f;
    public float ElevationScoreWeight { get; init; } = 12f;
}

public static class WatchtowerPlacementSolver
{
    public static IReadOnlyList<WatchtowerPlacement> Solve(HeatmapData data, WatchtowerPlacementParameters parameters)
    {
        if (Campaign.Current?.MapSceneWrapper == null)
        {
            return Array.Empty<WatchtowerPlacement>();
        }

        List<Town> towns = Town.AllTowns
            .Where(t => t?.Settlement != null && t.Settlement.GatePosition.Face.IsValid())
            .ToList();
        if (towns.Count == 0)
        {
            return Array.Empty<WatchtowerPlacement>();
        }

        Vec2[] cellWorldPositions = BuildCellWorldPositions(data.Header, data.Cells);
        int[] baseCandidateIndexes = BuildBaseCandidateIndexes(data.Cells, cellWorldPositions, parameters);
        float[] terrainHeightsByCellIndex = BuildTerrainHeightCache(data.Cells.Length, baseCandidateIndexes, cellWorldPositions);
        List<ThreatOrigin>[] threatOriginsByTownIndex = BuildThreatOriginsByTown(towns, parameters, data);
        TownSolveResult[] solveResults = new TownSolveResult[towns.Count];
        LoadingProcessHintTracker.BeginTask("Solving watchtower placement...", Math.Max(1, towns.Count));
        try
        {
            int completedTowns = 0;
            ParallelOptions outerParallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount)
            };
            Parallel.For(
                fromInclusive: 0,
                toExclusive: towns.Count,
                outerParallelOptions,
                localInit: () => new HeatmapAStarDistanceSolver(data.Header, data.Cells),
                body: (tIndex, loopState, distanceSolver) =>
                {
                    Town town = towns[tIndex];
                    Settlement townSettlement = town.Settlement;
                    CampaignVec2 townGate = townSettlement.GatePosition;
                    Vec2 townGateVec2 = townGate.ToVec2();
                    string townName = townSettlement.Name.ToString();
                    List<ThreatOrigin> threatOrigins = threatOriginsByTownIndex[tIndex];

                    List<Candidate> candidates = new List<Candidate>();
                    float minDistanceSquared = parameters.MinimumDistanceFromParentTown * parameters.MinimumDistanceFromParentTown;
                    float maxDistanceSquared = parameters.MaximumDistanceFromParentTown * parameters.MaximumDistanceFromParentTown;
                    List<int> filteredCandidateIndexes = new List<int>(baseCandidateIndexes.Length);
                    for (int i = 0; i < baseCandidateIndexes.Length; i++)
                    {
                        int cellIndex = baseCandidateIndexes[i];
                        Vec2 candidateWorld = cellWorldPositions[cellIndex];
                        float cd = townGateVec2.DistanceSquared(candidateWorld);
                        if (cd > maxDistanceSquared || cd < minDistanceSquared)
                        {
                            continue;
                        }

                        filteredCandidateIndexes.Add(cellIndex);
                    }

                    object candidateMergeLock = new object();
                    Parallel.For(
                        fromInclusive: 0,
                        toExclusive: filteredCandidateIndexes.Count,
                        localInit: () => (Solver: new HeatmapAStarDistanceSolver(data.Header, data.Cells), LocalCandidates: new List<Candidate>()),
                        body: (cellIndex, innerLoopState, localState) =>
                        {
                            int candidateIndex = filteredCandidateIndexes[cellIndex];
                            HeatmapCell cell = data.Cells[candidateIndex];
                            Vec2 candidateWorld = cellWorldPositions[candidateIndex];
                            CampaignVec2 candidateCampaign = new CampaignVec2(candidateWorld, isOnLand: true);
                            if (!candidateCampaign.Face.IsValid())
                            {
                                return localState;
                            }

                            if (!localState.Solver.TryGetPathDistance(
                                    townGateVec2,
                                    candidateWorld,
                                    parameters.MaximumDistanceFromParentTown,
                                    out float pathDistance))
                            {
                                return localState;
                            }

                            if (pathDistance < parameters.MinimumDistanceFromParentTown)
                            {
                                return localState;
                            }

                            int heatScore = cell.Distance;

                            float threatScore = EvaluateThreatScore(
                                candidateWorld,
                                pathDistance,
                                parameters,
                                threatOrigins,
                                localState.Solver);
                            float distanceBias = Clamp01(pathDistance / parameters.MaximumDistanceFromParentTown);
                            float combinedScore = threatScore * 1000f + heatScore * 0.5f + distanceBias;
                            float terrainHeight = terrainHeightsByCellIndex[candidateIndex];

                            localState.LocalCandidates.Add(new Candidate
                            {
                                Cell = cell,
                                HeatScore = heatScore,
                                PathDistance = pathDistance,
                                WorldPosition = candidateWorld,
                                ThreatScore = threatScore,
                                TerrainHeight = terrainHeight,
                                CombinedScore = combinedScore
                            });

                            return localState;
                        },
                        localFinally: localState =>
                        {
                            if (localState.LocalCandidates.Count == 0)
                            {
                                return;
                            }

                            lock (candidateMergeLock)
                            {
                                candidates.AddRange(localState.LocalCandidates);
                            }
                        });

                    ApplyRelativeElevationBonus(candidates, parameters.ElevationScoreWeight);
                    Candidate? best = null;
                    for (int i = 0; i < candidates.Count; i++)
                    {
                        Candidate candidate = candidates[i];
                        if (IsBetter(candidate, best))
                        {
                            best = candidate;
                        }
                    }

                    if (best != null)
                    {
                        solveResults[tIndex] = new TownSolveResult
                        {
                            Town = town,
                            TownName = townName,
                            BestCandidate = best
                        };
                    }

                    int done = Interlocked.Increment(ref completedTowns);
                    LoadingProcessHintTracker.ReportProgress(done);
                    return distanceSolver;
                },
                localFinally: _ => { });
        }
        finally
        {
            LoadingProcessHintTracker.CompleteTask();
        }

        List<WatchtowerPlacement> placements = new List<WatchtowerPlacement>();
        for (int i = 0; i < solveResults.Length; i++)
        {
            TownSolveResult? result = solveResults[i];
            if (result == null || result.BestCandidate == null)
            {
                continue;
            }

            Candidate best = result.BestCandidate;
            if (!IsFarEnoughFromExisting(placements, best.WorldPosition, parameters.MinimumDistanceBetweenWatchtowers))
            {
                continue;
            }

            Town town = result.Town;
            placements.Add(new WatchtowerPlacement
            {
                WatchtowerId = $"{result.TownName.ToLower()}_watchtower",
                WatchtowerName = $"{result.TownName} Watchtower",
                Position = best.WorldPosition,
                GatePosition = ComputeGatePosition(best.WorldPosition),
                WatchtowerBound = $"Settlement.{town.Settlement.StringId}",
                BackgroundCropPosition = 0,
                BackgroudMesh = $"gui_bg_castle_{town.Culture.Name.ToString().ToLower()}",
                WaitMesh = $"wait_{town.Culture.Name.ToString().ToLower()}_town",
            });
        }

        Dictionary<string, int> townOrderBySettlementId = new Dictionary<string, int>(towns.Count, StringComparer.Ordinal);
        for (int i = 0; i < towns.Count; i++)
        {
            townOrderBySettlementId[towns[i].Settlement.StringId] = i;
        }

        return placements
            .OrderBy(p => GetTownOrderFromBound(p.WatchtowerBound, townOrderBySettlementId))
            .ToList();
    }

    public static Task<IReadOnlyList<WatchtowerPlacement>> SolveAsync(HeatmapData data, WatchtowerPlacementParameters parameters)
    {
        return Task.Run(() => Solve(data, parameters));
    }

    public static void WritePlacementsToModuleSettlementsXml(
        IReadOnlyList<WatchtowerPlacement> placements,
        string templatePath,
        string outputPath)
    {
        XmlDocument template = new XmlDocument();
        template.Load(templatePath);
        XmlNode? templateSettlement = template.SelectSingleNode("/Settlements/Settlement");
        if (templateSettlement == null)
        {
            throw new InvalidDataException("Template settlements XML does not contain a Settlement node.");
        }

        XmlDocument output = new XmlDocument();
        XmlDeclaration declaration = output.CreateXmlDeclaration("1.0", "utf-8", null);
        output.AppendChild(declaration);
        XmlElement root = output.CreateElement("Settlements");
        output.AppendChild(root);

        foreach (WatchtowerPlacement placement in placements)
        {
            XmlNode clonedNode = output.ImportNode(templateSettlement, deep: true);
            if (clonedNode is not XmlElement settlementElement)
            {
                continue;
            }
            settlementElement.SetAttribute("id", placement.WatchtowerId);
            settlementElement.SetAttribute("name", placement.WatchtowerName);
            settlementElement.SetAttribute("posX", placement.Position.X.ToString("0.###", CultureInfo.InvariantCulture));
            settlementElement.SetAttribute("posY", placement.Position.Y.ToString("0.###", CultureInfo.InvariantCulture));
            settlementElement.SetAttribute("gate_posX", placement.GatePosition.X.ToString("0.###", CultureInfo.InvariantCulture));
            settlementElement.SetAttribute("gate_posY", placement.GatePosition.Y.ToString("0.###", CultureInfo.InvariantCulture));

            XmlNode? customSettlementCompNode = settlementElement.SelectSingleNode("./Components/CustomSettlementComponent");
            if (customSettlementCompNode is XmlElement scsElement)
            {
                scsElement.SetAttribute("id", $"{placement.WatchtowerId}_comp");
                scsElement.SetAttribute("component_name", "WatchtowerSettlementComponent");
                scsElement.SetAttribute("bound", placement.WatchtowerBound);
                scsElement.SetAttribute("background_crop_position", placement.BackgroundCropPosition.ToString("0.#", CultureInfo.InvariantCulture));
                scsElement.SetAttribute("background_mesh", placement.BackgroudMesh);
                scsElement.SetAttribute("wait_mesh", placement.WaitMesh);
            }

            root.AppendChild(settlementElement);
        }

        string? outputDirectory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        XmlWriterSettings settings = new XmlWriterSettings
        {
            Indent = true,
            IndentChars = "\t",
            NewLineChars = "\n",
            NewLineHandling = NewLineHandling.Replace,
            NewLineOnAttributes = true
        };
        using XmlWriter writer = XmlWriter.Create(outputPath, settings);
        output.Save(writer);
    }

    public static string GetWatchtowerTemplatePath()
    {
        return Path.Combine(ModuleHelper.GetModuleFullPath("WatchtowerNetwork"), "ModuleData", "settlements_ostican-watchtower.xml");
    }

    public static string GetWatchtowerOutputSettlementsPath()
    {
        return Path.Combine(ModuleHelper.GetModuleFullPath("WatchtowerNetwork"), "ModuleData", "settlements.xml");
    }

    private static Vec2 GridToWorld(HeatmapHeader header, ushort gridX, ushort gridY)
    {
        float x = header.MinX + (gridX * header.GridStep);
        float y = header.MinY + (gridY * header.GridStep);
        return new Vec2(x, y);
    }

    private static Vec2[] BuildCellWorldPositions(HeatmapHeader header, HeatmapCell[] cells)
    {
        Vec2[] worldPositions = new Vec2[cells.Length];
        for (int i = 0; i < cells.Length; i++)
        {
            worldPositions[i] = GridToWorld(header, cells[i].PosX, cells[i].PosY);
        }

        return worldPositions;
    }

    private static int[] BuildBaseCandidateIndexes(
        HeatmapCell[] cells,
        Vec2[] cellWorldPositions,
        WatchtowerPlacementParameters parameters)
    {
        List<int> indexes = new List<int>(cells.Length / 4);
        for (int i = 0; i < cells.Length; i++)
        {
            HeatmapCell cell = cells[i];
            if (!cell.IsLand || cell.Distance <= 0)
            {
                continue;
            }

            if (parameters.CandidateStride > 1 &&
                ((cell.PosX % parameters.CandidateStride) != 0 || (cell.PosY % parameters.CandidateStride) != 0))
            {
                continue;
            }

            CampaignVec2 candidateCampaign = new CampaignVec2(cellWorldPositions[i], isOnLand: true);
            if (!candidateCampaign.Face.IsValid())
            {
                continue;
            }

            indexes.Add(i);
        }

        return indexes.ToArray();
    }

    private static float[] BuildTerrainHeightCache(int cellCount, int[] baseCandidateIndexes, Vec2[] cellWorldPositions)
    {
        float[] terrainHeights = new float[cellCount];
        for (int i = 0; i < cellCount; i++)
        {
            terrainHeights[i] = 0f;
        }

        for (int i = 0; i < baseCandidateIndexes.Length; i++)
        {
            int cellIndex = baseCandidateIndexes[i];
            Campaign.Current.MapSceneWrapper.GetTerrainHeightAndNormal(cellWorldPositions[cellIndex], out float terrainHeight, out _);
            terrainHeights[cellIndex] = terrainHeight;
        }

        return terrainHeights;
    }

    private static List<ThreatOrigin>[] BuildThreatOriginsByTown(
        List<Town> towns,
        WatchtowerPlacementParameters parameters,
        HeatmapData data)
    {
        int townCount = towns.Count;
        Vec2[] gates = new Vec2[townCount];
        string[] townIds = new string[townCount];
        for (int i = 0; i < townCount; i++)
        {
            gates[i] = towns[i].Settlement.GatePosition.ToVec2();
            townIds[i] = towns[i].Settlement.StringId;
        }

        float[,] pairDistances = new float[townCount, townCount];
        for (int i = 0; i < townCount; i++)
        {
            pairDistances[i, i] = 0f;
            for (int j = i + 1; j < townCount; j++)
            {
                pairDistances[i, j] = float.PositiveInfinity;
                pairDistances[j, i] = float.PositiveInfinity;
            }
        }

        Parallel.For(
            fromInclusive: 0,
            toExclusive: townCount,
            localInit: () => new HeatmapAStarDistanceSolver(data.Header, data.Cells),
            body: (i, _, solver) =>
            {
                for (int j = i + 1; j < townCount; j++)
                {
                    if (solver.TryGetPathDistance(gates[i], gates[j], Campaign.PathFindingMaxCostLimit, out float distance))
                    {
                        pairDistances[i, j] = distance;
                        pairDistances[j, i] = distance;
                    }
                }

                return solver;
            },
            localFinally: _ => { });

        List<ThreatOrigin>[] originsByTown = new List<ThreatOrigin>[townCount];
        for (int parentIndex = 0; parentIndex < townCount; parentIndex++)
        {
            List<ThreatOrigin> origins = new List<ThreatOrigin>();
            for (int originIndex = 0; originIndex < townCount; originIndex++)
            {
                if (originIndex == parentIndex)
                {
                    continue;
                }

                float pathDistance = pairDistances[originIndex, parentIndex];
                if (float.IsPositiveInfinity(pathDistance))
                {
                    continue;
                }

                origins.Add(new ThreatOrigin
                {
                    TownId = townIds[originIndex],
                    GateWorld = gates[originIndex],
                    PathDistanceToParent = pathDistance
                });
            }

            originsByTown[parentIndex] = origins
                .OrderBy(o => o.PathDistanceToParent)
                .ThenBy(o => o.TownId, StringComparer.Ordinal)
                .Take(Math.Max(1, parameters.MaximumThreatOriginsPerTown))
                .ToList();
        }

        return originsByTown;
    }

    private static Vec2 ComputeGatePosition(Vec2 watchtowerPos)
    {
        Campaign.Current.MapSceneWrapper.GetTerrainHeightAndNormal(watchtowerPos, out _, out Vec3 terrainNormal);
        Vec2 downhillDirection = new Vec2(terrainNormal.x, terrainNormal.y);
        if (downhillDirection.Normalize() > 0.001f)
        {
            return watchtowerPos + downhillDirection * 1.1f;
        }

        return watchtowerPos;
    }

    private static bool IsFarEnoughFromExisting(
        IReadOnlyList<WatchtowerPlacement> existing,
        Vec2 candidate,
        float minDistance)
    {
        float minDistanceSquared = minDistance * minDistance;
        for (int i = 0; i < existing.Count; i++)
        {
            if (existing[i].Position.DistanceSquared(candidate) < minDistanceSquared)
            {
                return false;
            }
        }

        return true;
    }

    private static int GetTownOrderFromBound(string watchtowerBound, IReadOnlyDictionary<string, int> townOrderBySettlementId)
    {
        if (!TryExtractSettlementIdFromBound(watchtowerBound, out string settlementId))
        {
            return int.MaxValue;
        }

        return townOrderBySettlementId.TryGetValue(settlementId, out int index) ? index : int.MaxValue;
    }

    private static bool TryExtractSettlementIdFromBound(string watchtowerBound, out string settlementId)
    {
        const string prefix = "Settlement.";
        if (!string.IsNullOrEmpty(watchtowerBound) &&
            watchtowerBound.StartsWith(prefix, StringComparison.Ordinal))
        {
            settlementId = watchtowerBound.Substring(prefix.Length);
            return settlementId.Length > 0;
        }

        settlementId = string.Empty;
        return false;
    }

    private static bool IsBetter(Candidate left, Candidate? right)
    {
        if (right == null)
        {
            return true;
        }

        if (Math.Abs(left.CombinedScore - right.CombinedScore) > 0.001f)
        {
            return left.CombinedScore > right.CombinedScore;
        }

        if (Math.Abs(left.ThreatScore - right.ThreatScore) > 0.001f)
        {
            return left.ThreatScore > right.ThreatScore;
        }

        if (left.HeatScore != right.HeatScore)
        {
            return left.HeatScore > right.HeatScore;
        }

        if (Math.Abs(left.TerrainHeight - right.TerrainHeight) > 0.01f)
        {
            return left.TerrainHeight > right.TerrainHeight;
        }

        if (Math.Abs(left.PathDistance - right.PathDistance) > 0.001f)
        {
            return left.PathDistance > right.PathDistance;
        }

        if (left.Cell.PosY != right.Cell.PosY)
        {
            return left.Cell.PosY < right.Cell.PosY;
        }

        return left.Cell.PosX < right.Cell.PosX;
    }

    private static float EvaluateThreatScore(
        Vec2 candidateWorld,
        float candidateToParentPathDistance,
        WatchtowerPlacementParameters parameters,
        List<ThreatOrigin> origins,
        HeatmapAStarDistanceSolver distanceSolver)
    {
        if (origins.Count == 0)
        {
            return 0f;
        }

        CampaignVec2 candidatePos = new CampaignVec2(candidateWorld, isOnLand: true);
        if (!candidatePos.Face.IsValid())
        {
            return 0f;
        }

        float tolerance = Math.Max(0.01f, parameters.InterceptDetourTolerance);
        float score = 0f;
        for (int i = 0; i < origins.Count; i++)
        {
            ThreatOrigin origin = origins[i];
            float lowerBound = origin.GateWorld.Distance(candidateWorld) + candidateToParentPathDistance;
            if (lowerBound > origin.PathDistanceToParent + tolerance)
            {
                continue;
            }

            if (!distanceSolver.TryGetPathDistance(
                    origin.GateWorld,
                    candidateWorld,
                    origin.PathDistanceToParent + tolerance,
                    out float originToCandidateDistance))
            {
                continue;
            }

            float detour = originToCandidateDistance + candidateToParentPathDistance - origin.PathDistanceToParent;
            if (detour > tolerance)
            {
                continue;
            }

            float directness = 1f - Clamp01(detour / tolerance);
            score += directness;
        }

        return score;
    }

    private static void ApplyRelativeElevationBonus(List<Candidate> candidates, float elevationScoreWeight)
    {
        if (candidates.Count == 0 || elevationScoreWeight <= 0f)
        {
            return;
        }

        float minHeight = candidates[0].TerrainHeight;
        float maxHeight = minHeight;
        for (int i = 1; i < candidates.Count; i++)
        {
            float height = candidates[i].TerrainHeight;
            if (height < minHeight)
            {
                minHeight = height;
            }

            if (height > maxHeight)
            {
                maxHeight = height;
            }
        }

        float range = maxHeight - minHeight;
        if (range <= 0.01f)
        {
            return;
        }

        for (int i = 0; i < candidates.Count; i++)
        {
            Candidate candidate = candidates[i];
            float elevation01 = (candidate.TerrainHeight - minHeight) / range;
            candidate.CombinedScore += elevation01 * elevationScoreWeight;
        }
    }

    private static float Clamp01(float value)
    {
        if (value < 0f)
        {
            return 0f;
        }

        if (value > 1f)
        {
            return 1f;
        }

        return value;
    }

    private sealed class ThreatOrigin
    {
        public string TownId { get; init; } = string.Empty;
        public Vec2 GateWorld { get; init; }
        public float PathDistanceToParent { get; init; }
    }

    private sealed class Candidate
    {
        public HeatmapCell Cell { get; init; }
        public int HeatScore { get; init; }
        public float PathDistance { get; init; }
        public Vec2 WorldPosition { get; init; }
        public float ThreatScore { get; init; }
        public float TerrainHeight { get; init; }
        public float CombinedScore { get; set; }
    }

    private sealed class TownSolveResult
    {
        public Town Town { get; init; } = null!;
        public string TownName { get; init; } = string.Empty;
        public Candidate? BestCandidate { get; init; }
    }
}

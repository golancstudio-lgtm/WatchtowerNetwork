using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SandBox;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using TaleWorlds.SaveSystem;
using TaleWorlds.SaveSystem.Load;

namespace WatchtowerNetwork.Heatmaps;

public sealed class HeatmapCampaignBehavior : CampaignBehaviorBase
{
    private static readonly object SessionWorkSync = new object();
    private static Task? _sessionLaunchWorkTask;
    private static Campaign? _sessionWorkCampaign;

    public static bool IsSessionLaunchWorkPending
    {
        get
        {
            Task? task = Volatile.Read(ref _sessionLaunchWorkTask);
            return task != null && !task.IsCompleted;
        }
    }

    public static bool IsSessionLaunchWorkCompleted
    {
        get
        {
            Task? task = Volatile.Read(ref _sessionLaunchWorkTask);
            return task != null && task.IsCompleted;
        }
    }

    public static void EnsureSessionLaunchWorkStarted()
    {
        Campaign? currentCampaign = Campaign.Current;
        if (currentCampaign == null)
        {
            return;
        }

        lock (SessionWorkSync)
        {
            // Start once per campaign instance. Re-starting every completed task can
            // keep FinishLoadingFifthStep pinned forever because the patch polls this each tick.
            if (_sessionLaunchWorkTask == null || !ReferenceEquals(_sessionWorkCampaign, currentCampaign))
            {
                _sessionWorkCampaign = currentCampaign;
                _sessionLaunchWorkTask = RunSessionLaunchWorkAsync();
            }
        }
    }

    public override void RegisterEvents()
    {
    }

    public override void SyncData(IDataStore dataStore)
    {
    }

    private static async Task RunSessionLaunchWorkAsync()
    {
        try
        {
            if (!await WaitForCampaignReadinessAsync().ConfigureAwait(false))
            {
                return;
            }

            if (!HeatmapPathResolver.TryBuildFileContext(out HeatmapFileContext context))
            {
                return;
            }

            HeatmapPathResolver.EnsureDirectory(context);
            await EnsureHeatmapReadyAsync(context);

            string cachedPlacementPath = HeatmapPathResolver.GetPlacementFilePath(context);
            if (!File.Exists(cachedPlacementPath))
            {
                await GenerateAndStorePlacementAsync(context, cachedPlacementPath);
            }

            bool hasReplacedSettlementsFile = ReplaceModuleSettlementsWith(cachedPlacementPath);
            if (hasReplacedSettlementsFile)
            {
                ReloadCurrentCampaignSave();
            }
        }
        finally
        {
            LoadingProcessHintTracker.Clear();
        }
    }

    private static async Task<bool> WaitForCampaignReadinessAsync()
    {
        const int maxAttempts = 400; // ~20s total
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            if (Campaign.Current?.MapSceneWrapper != null && Town.AllTowns.Count > 0)
            {
                return true;
            }

            await Task.Delay(50).ConfigureAwait(false);
        }

        return Campaign.Current?.MapSceneWrapper != null && Town.AllTowns.Count > 0;
    }

    private static async Task EnsureHeatmapReadyAsync(HeatmapFileContext context)
    {
        bool canUseExistingFile = File.Exists(context.FilePath) &&
                                  string.Equals(Path.GetFileName(context.FilePath), context.FileName, StringComparison.Ordinal);

        if (canUseExistingFile &&
            HeatmapBinarySerializer.TryReadHeader(context.FilePath, out HeatmapHeader? header) &&
            header != null &&
            HeatmapBinarySerializer.IsCompatibleWithCurrentGame(header, context.GameVersionTag, context.MapModuleId) &&
            HeatmapBinarySerializer.TryLoad(context.FilePath, out HeatmapData? loadedData) &&
            loadedData != null)
        {
            HeatmapDataHolder.Set(loadedData);
            return;
        }

        HeatmapData generatedData = await HeatmapBuilder.BuildAsync(context.MapModuleId, context.GameVersionTag);
        HeatmapBinarySerializer.Save(context.FilePath, generatedData);
        HeatmapDataHolder.Set(generatedData);
    }

    private static async Task GenerateAndStorePlacementAsync(HeatmapFileContext context, string cachedPlacementPath)
    {
        if (HeatmapDataHolder.Current == null)
        {
            await EnsureHeatmapReadyAsync(context);
        }

        if (HeatmapDataHolder.Current == null)
        {
            return;
        }

        WatchtowerPlacementParameters parameters = new WatchtowerPlacementParameters();
        IReadOnlyList<WatchtowerPlacement> placements = await WatchtowerPlacementSolver.SolveAsync(HeatmapDataHolder.Current, parameters);
        string templatePath = WatchtowerPlacementSolver.GetWatchtowerTemplatePath();
        WatchtowerPlacementSolver.WritePlacementsToModuleSettlementsXml(placements, templatePath, cachedPlacementPath);
    }

    private static bool ReplaceModuleSettlementsWith(string sourcePlacementPath)
    {
        string moduleSettlementsPath = WatchtowerPlacementSolver.GetWatchtowerOutputSettlementsPath();
        if (File.Exists(moduleSettlementsPath) && AreFilesIdentical(sourcePlacementPath, moduleSettlementsPath))
        {
            return false;
        }

        if (File.Exists(moduleSettlementsPath))
        {
            File.Delete(moduleSettlementsPath);
        }

        string? moduleDirectory = Path.GetDirectoryName(moduleSettlementsPath);
        if (!string.IsNullOrEmpty(moduleDirectory))
        {
            Directory.CreateDirectory(moduleDirectory);
        }

        File.Copy(sourcePlacementPath, moduleSettlementsPath, overwrite: false);
        return true;
    }

    private static bool AreFilesIdentical(string firstPath, string secondPath)
    {
        FileInfo firstInfo = new FileInfo(firstPath);
        FileInfo secondInfo = new FileInfo(secondPath);
        if (firstInfo.Length != secondInfo.Length)
        {
            return false;
        }

        const int bufferSize = 81920;
        using FileStream firstStream = File.OpenRead(firstPath);
        using FileStream secondStream = File.OpenRead(secondPath);
        byte[] firstBuffer = new byte[bufferSize];
        byte[] secondBuffer = new byte[bufferSize];

        while (true)
        {
            int firstRead = firstStream.Read(firstBuffer, 0, firstBuffer.Length);
            int secondRead = secondStream.Read(secondBuffer, 0, secondBuffer.Length);
            if (firstRead != secondRead)
            {
                return false;
            }

            if (firstRead == 0)
            {
                return true;
            }

            for (int i = 0; i < firstRead; i++)
            {
                if (firstBuffer[i] != secondBuffer[i])
                {
                    return false;
                }
            }
        }
    }

    private static void ReloadCurrentCampaignSave()
    {
        string saveName = MBSaveLoad.ActiveSaveSlotName;
        if (string.IsNullOrEmpty(saveName))
        {
            saveName = BannerlordConfig.LatestSaveGameName;
        }

        if (string.IsNullOrEmpty(saveName))
        {
            return;
        }

        SaveGameFileInfo? saveInfo = MBSaveLoad.GetSaveFileWithName(saveName);
        if (saveInfo == null)
        {
            return;
        }

        SandBoxSaveHelper.TryLoadSave(saveInfo, StartLoadedCampaign);
    }

    private static void StartLoadedCampaign(LoadResult loadResult)
    {
        MBGameManager.StartNewGame(new SandBoxGameManager(loadResult));
    }
}

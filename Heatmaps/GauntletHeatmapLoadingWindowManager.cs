using HarmonyLib;
using SandBox.GauntletUI;
using System.Diagnostics;
using TaleWorlds.MountAndBlade.GauntletUI;

namespace WatchtowerNetwork.Heatmaps;

public class GauntletHeatmapLoadingWindowManager : GauntletDefaultLoadingWindowManager
{
    public Traverse VM_TitleText => Traverse.Create(this).Field("_loadingWindowViewModel").Property("TitleText");
    public Traverse VM_DescriptionText => Traverse.Create(this).Field("_loadingWindowViewModel").Property("DescriptionText");
    private Stopwatch stopwatch;

    public GauntletHeatmapLoadingWindowManager() : base()
    {
        stopwatch = new Stopwatch();
    }

    protected override void OnTick(float dt)
    {
        base.OnLateTick(dt);
        if (LoadingProcessHintTracker.TryGetSnapshot(out string taskName, out int progressPercent, out string etaText))
        {
            if (!stopwatch.IsRunning)
            {
                stopwatch.Start();
            }
            VM_TitleText.SetValue(taskName);
            VM_DescriptionText.SetValue($"Progress: {progressPercent}%\nETA: {etaText}\nElapsed: {stopwatch.Elapsed:hh\\:mm\\:ss}");
        }
    }
}

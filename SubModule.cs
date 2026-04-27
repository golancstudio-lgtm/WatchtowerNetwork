using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ObjectSystem;
using WatchtowerNetwork.Heatmaps;
using WatchtowerNetwork.Patches;
using WatchtowerNetwork.WatchtowerSettlement;
using WatchtowerNetwork.WatchtowerSettlement.CampaignBehaviors;

namespace WatchtowerNetwork;

public class SubModule : MBSubModuleBase
{
    private const string HarmonyId = "com.watchtower.network";
    private bool _initializedLoadingCategory;

    protected override void OnApplicationTick(float dt)
    {
        base.OnApplicationTick(dt);
        if (!_initializedLoadingCategory)
        {
            LoadingWindow.InitializeWith<GauntletHeatmapLoadingWindowManager>();
            _initializedLoadingCategory = true;
        }
    }

    protected override void OnGameStart(Game game, IGameStarter gameStarter)
    {
        base.OnGameStart(game, gameStarter);
        if (gameStarter is CampaignGameStarter campaignStarter)
        {
            MBObjectManager.Instance.RegisterType<WatchtowerSettlementComponent>("WatchtowerSettlementComponent", "WatchtowerSettlementComponents", 112358);
            campaignStarter.AddBehavior(new HeatmapCampaignBehavior());
            campaignStarter.AddBehavior(new PlayerWatchtowerVisitCampaignBehavior());
            campaignStarter.AddBehavior(new WatchtowerManagerCampaignBehavior());
            campaignStarter.AddBehavior(new WatchtowerMessengersManagerCampaignBehavior());
        }
    }

    protected override void OnSubModuleLoad()
    {
        base.OnSubModuleLoad();
        new Harmony(HarmonyId).PatchAll();
    }

    protected override void OnSubModuleUnloaded()
    {
        new Harmony(HarmonyId).UnpatchAll();
        base.OnSubModuleUnloaded();
    }
}

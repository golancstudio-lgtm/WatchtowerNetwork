using HarmonyLib;
using SandBox;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using WatchtowerNetwork.Heatmaps;

namespace WatchtowerNetwork.Patches;

[HarmonyPatch(typeof(GameLoadingState), "OnTick")]
internal static class GameLoadingStateOnTick
{

    private static Traverse? traverse, lf;
    private static MBGameManager? gl;

    private static bool Prefix(GameLoadingState __instance, float dt)
    {
        if (traverse is null || lf is null || gl is null)
        {
            traverse = Traverse.Create(__instance);
            lf = traverse.Field("_loadingFinished");
            gl = (MBGameManager)traverse.Field("_gameLoader").GetValue();
        }
        if (!((bool)lf.GetValue()))
        {
            lf.SetValue(gl.DoLoadingForGameManager());
        }
        else
        {
            HeatmapCampaignBehavior.EnsureSessionLaunchWorkStarted();
            if (HeatmapCampaignBehavior.IsSessionLaunchWorkCompleted)
            {
                GameStateManager.Current = Game.Current.GameStateManager;
                gl.OnLoadFinished();
                traverse = null;
                lf = null;
                gl = null;
            }
        }
        return false;
    }
}

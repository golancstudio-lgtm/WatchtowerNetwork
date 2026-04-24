using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using WatchtowerNetwork.WatchtowerSettlement;

namespace WatchtowerNetwork.Patches;


internal static class SettlementGettersPatch
{
    [HarmonyPatch(typeof(Settlement), nameof(Settlement.OwnerClan), MethodType.Getter)]
    internal static class OwnerPatch
    {
        private static bool Prefix(Settlement __instance, ref Clan __result)
        {
            if (__instance.TryGetWatchtower(out WatchtowerSettlement.WatchtowerSettlementComponent? watchtower) && watchtower is not null && watchtower.Bound is not null)
            {
                __result = watchtower.Bound.OwnerClan;
                return false;
            }
            else
            {
                return true;
            }
        }
    }
}

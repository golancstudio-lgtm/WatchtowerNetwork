using HarmonyLib;
using System;
using Helpers;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Settlements;
using WatchtowerNetwork.WatchtowerSettlement;

namespace WatchtowerNetwork.Patches;

[HarmonyPatch(typeof(DefaultEncounterGameMenuModel), nameof(DefaultEncounterGameMenuModel.GetEncounterMenu))]
internal class DefaultEncounterGameMenuModelGetEncounterMenuPatch
{
    private static void Postfix(PartyBase attackerParty, PartyBase defenderParty, ref string __result)
    {
        if (!string.Equals(__result, "", StringComparison.Ordinal))
        {
            return;
        }

        if (defenderParty is null)
        {
            return;
        }

        PartyBase encounteredPartyBase = MapEventHelper.GetEncounteredPartyBase(attackerParty, defenderParty);
        if (!encounteredPartyBase.IsSettlement)
        {
            return;
        }

        Settlement settlement = encounteredPartyBase.Settlement;
        if (settlement.SettlementComponent is WatchtowerSettlementComponent)
        {
            __result = "watchtower_place";
        }
    }
}
using HarmonyLib;
using SandBox.GauntletUI.Encyclopedia;
using WatchtowerNetwork.WatchtowerSettlement.UI;

namespace WatchtowerNetwork.Patches;

[HarmonyPatch(typeof(GauntletMapEncyclopediaView), nameof(GauntletMapEncyclopediaView.CloseEncyclopedia))]
internal static class GauntletMapEncyclopediaClosePatch
{
    private static void Postfix()
    {
        WatchtowerReportsUIController.NotifyEncyclopediaClosed();
    }
}

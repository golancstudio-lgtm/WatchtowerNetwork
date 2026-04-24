using TaleWorlds.CampaignSystem.Settlements;

namespace WatchtowerNetwork.WatchtowerSettlement;

internal static class WatchtowerRadiusOverlayState
{
    public static Settlement? Current { get; private set; }

    public static void SetCurrent(Settlement? settlement)
    {
        Current = settlement;
    }

    public static void Clear()
    {
        Current = null;
    }
}

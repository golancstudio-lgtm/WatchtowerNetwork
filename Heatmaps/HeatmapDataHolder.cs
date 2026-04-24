namespace WatchtowerNetwork.Heatmaps;

public static class HeatmapDataHolder
{
    public static HeatmapData? Current { get; private set; }

    public static bool HasData => Current != null;

    public static void Set(HeatmapData data)
    {
        Current = data;
    }

    public static void Clear()
    {
        Current = null;
    }
}

namespace WatchtowerNetwork.Heatmaps;

public sealed class HeatmapHeader
{
    public const string Magic = "WNHM";
    public const ushort FormatVersion = 9;

    public string MapModuleId { get; init; } = string.Empty;
    public string GameVersion { get; init; } = string.Empty;
    public int GridWidth { get; init; }
    public int GridHeight { get; init; }
    public float GridStep { get; init; }
    public float MinX { get; init; }
    public float MinY { get; init; }
    public float MaxX { get; init; }
    public float MaxY { get; init; }
}

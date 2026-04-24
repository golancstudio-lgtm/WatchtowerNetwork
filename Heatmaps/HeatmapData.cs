namespace WatchtowerNetwork.Heatmaps;

public sealed class HeatmapData
{
    public HeatmapHeader Header { get; }
    public HeatmapCell[] Cells { get; }

    public HeatmapData(HeatmapHeader header, HeatmapCell[] cells)
    {
        Header = header;
        Cells = cells;
    }
}

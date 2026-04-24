namespace WatchtowerNetwork.Heatmaps;

public struct HeatmapCell
{
    public ushort PosX;
    public ushort PosY;
    public bool IsLand;
    public byte Distance;

    public HeatmapCell(ushort posX, ushort posY, bool isLand, byte distance)
    {
        PosX = posX;
        PosY = posY;
        IsLand = isLand;
        Distance = distance;
    }
}

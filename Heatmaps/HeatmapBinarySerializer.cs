using System;
using System.IO;
using System.Text;

namespace WatchtowerNetwork.Heatmaps;

public static class HeatmapBinarySerializer
{
    public static bool TryReadHeader(string filePath, out HeatmapHeader? header)
    {
        header = null;
        if (!File.Exists(filePath))
        {
            return false;
        }

        try
        {
            using FileStream stream = File.OpenRead(filePath);
            using BinaryReader reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false);

            if (!TryReadAndValidateMagicAndFormat(reader))
            {
                return false;
            }

            string mapModuleId = ReadString(reader);
            string gameVersion = ReadString(reader);
            int gridWidth = reader.ReadInt32();
            int gridHeight = reader.ReadInt32();
            float gridStep = reader.ReadSingle();
            float minX = reader.ReadSingle();
            float minY = reader.ReadSingle();
            float maxX = reader.ReadSingle();
            float maxY = reader.ReadSingle();
            _ = reader.ReadInt32(); // Cell count, kept in stream for full reads.

            header = new HeatmapHeader
            {
                MapModuleId = mapModuleId,
                GameVersion = gameVersion,
                GridWidth = gridWidth,
                GridHeight = gridHeight,
                GridStep = gridStep,
                MinX = minX,
                MinY = minY,
                MaxX = maxX,
                MaxY = maxY
            };

            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool TryLoad(string filePath, out HeatmapData? data)
    {
        data = null;
        if (!File.Exists(filePath))
        {
            return false;
        }

        try
        {
            using FileStream stream = File.OpenRead(filePath);
            using BinaryReader reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false);
            if (!TryReadAndValidateMagicAndFormat(reader))
            {
                return false;
            }

            HeatmapHeader header = new HeatmapHeader
            {
                MapModuleId = ReadString(reader),
                GameVersion = ReadString(reader),
                GridWidth = reader.ReadInt32(),
                GridHeight = reader.ReadInt32(),
                GridStep = reader.ReadSingle(),
                MinX = reader.ReadSingle(),
                MinY = reader.ReadSingle(),
                MaxX = reader.ReadSingle(),
                MaxY = reader.ReadSingle()
            };

            int cellCount = reader.ReadInt32();
            if (cellCount < 0)
            {
                return false;
            }

            HeatmapCell[] cells = new HeatmapCell[cellCount];
            for (int i = 0; i < cellCount; i++)
            {
                ushort posX = reader.ReadUInt16();
                ushort posY = reader.ReadUInt16();
                bool isLand = reader.ReadBoolean();
                byte distance = reader.ReadByte();
                cells[i] = new HeatmapCell(posX, posY, isLand, distance);
            }

            data = new HeatmapData(header, cells);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static void Save(string filePath, HeatmapData data)
    {
        string? directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using FileStream stream = File.Open(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        using BinaryWriter writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: false);

        WriteString(writer, HeatmapHeader.Magic);
        writer.Write(HeatmapHeader.FormatVersion);
        WriteString(writer, data.Header.MapModuleId);
        WriteString(writer, data.Header.GameVersion);
        writer.Write(data.Header.GridWidth);
        writer.Write(data.Header.GridHeight);
        writer.Write(data.Header.GridStep);
        writer.Write(data.Header.MinX);
        writer.Write(data.Header.MinY);
        writer.Write(data.Header.MaxX);
        writer.Write(data.Header.MaxY);
        writer.Write(data.Cells.Length);

        for (int i = 0; i < data.Cells.Length; i++)
        {
            HeatmapCell cell = data.Cells[i];
            writer.Write(cell.PosX);
            writer.Write(cell.PosY);
            writer.Write(cell.IsLand);
            writer.Write(cell.Distance);
        }
    }

    public static bool IsCompatibleWithCurrentGame(HeatmapHeader header, string currentGameVersion, string currentMapModuleId)
    {
        return string.Equals(header.MapModuleId, currentMapModuleId, StringComparison.Ordinal) &&
               string.Equals(header.GameVersion, currentGameVersion, StringComparison.Ordinal);
    }

    private static bool TryReadAndValidateMagicAndFormat(BinaryReader reader)
    {
        string magic = ReadString(reader);
        if (!string.Equals(magic, HeatmapHeader.Magic, StringComparison.Ordinal))
        {
            return false;
        }

        ushort formatVersion = reader.ReadUInt16();
        return formatVersion == HeatmapHeader.FormatVersion;
    }

    private static void WriteString(BinaryWriter writer, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }

    private static string ReadString(BinaryReader reader)
    {
        int length = reader.ReadInt32();
        if (length < 0 || length > 1024 * 1024)
        {
            throw new InvalidDataException("Invalid string length.");
        }

        byte[] bytes = reader.ReadBytes(length);
        if (bytes.Length != length)
        {
            throw new EndOfStreamException("Unexpected end of stream.");
        }

        return Encoding.UTF8.GetString(bytes);
    }
}

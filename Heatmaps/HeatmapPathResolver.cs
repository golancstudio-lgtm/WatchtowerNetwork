using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.ModuleManager;
using TaleWorlds.MountAndBlade;
using SystemPath = System.IO.Path;

namespace WatchtowerNetwork.Heatmaps;

public sealed class HeatmapFileContext
{
    public string DocumentsRoot { get; init; } = string.Empty;
    public string DirectoryPath { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public string FilePath { get; init; } = string.Empty;
    public string GameVersionTag { get; init; } = string.Empty;
    public string MapModuleId { get; init; } = string.Empty;
}

public static class HeatmapPathResolver
{
    private const string ConfigDirectoryName = "Configs";
    private const string PlacementFilePrefix = "watchtower_placements";

    public static bool TryBuildFileContext(out HeatmapFileContext context)
    {
        context = new HeatmapFileContext();
        if (Campaign.Current?.MapSceneWrapper == null)
        {
            return false;
        }

        string documentsRoot = GetBannerlordDocumentsRoot();
        if (string.IsNullOrEmpty(documentsRoot))
        {
            return false;
        }

        string mapModuleId = GetMapModuleId();
        string gameVersionTag = GetMapModuleVersion();
        string mapName = !string.IsNullOrWhiteSpace(mapModuleId) ? mapModuleId : "UnknownMap";
        string directoryPath = SystemPath.Combine(documentsRoot, ConfigDirectoryName, "WatchtowerNetwork", "HeatMaps", mapName);
        string fileName = $"town_distances_heatmap_{gameVersionTag}.bin";
        string filePath = SystemPath.Combine(directoryPath, fileName);

        context = new HeatmapFileContext
        {
            DocumentsRoot = documentsRoot,
            DirectoryPath = directoryPath,
            FileName = fileName,
            FilePath = filePath,
            GameVersionTag = gameVersionTag,
            MapModuleId = mapModuleId
        };
        return true;
    }

    public static string GetGameVersionTag()
    {
        ApplicationVersion version = Utilities.GetApplicationVersionWithBuildNumber();
        return $"{version.Major}.{version.Minor}.{version.Revision}";
    }

    public static void EnsureDirectory(HeatmapFileContext context)
    {
        Directory.CreateDirectory(context.DirectoryPath);
    }

    public static string GetPlacementFilePath(HeatmapFileContext context)
    {
        string fileName = $"{PlacementFilePrefix}_{context.GameVersionTag}.xml";
        return SystemPath.Combine(context.DirectoryPath, fileName);
    }

    private static string GetBannerlordDocumentsRoot()
    {
        try
        {
            PlatformFilePath probeFile = new PlatformFilePath(EngineFilePaths.ConfigsPath, "watchtowernetwork_probe.tmp");
            string configPath = FileHelper.GetFileFullPath(probeFile);
            string? configsDirectory = SystemPath.GetDirectoryName(configPath);
            if (string.IsNullOrEmpty(configsDirectory))
            {
                return string.Empty;
            }

            string? root = Directory.GetParent(configsDirectory)?.FullName;
            if (!string.IsNullOrEmpty(root))
            {
                return root ?? string.Empty;
            }
        }
        catch
        {
            // Fallback below.
        }

        string documents = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
        return SystemPath.Combine(documents, Utilities.GetApplicationName());
    }

    private static string GetMapModuleId()
    {
        try
        {
            string modulePath = GetMapModulePath();
            if (string.IsNullOrEmpty(modulePath))
            {
                return string.Empty;
            }

            return ReadSubModuleValue(modulePath, "Id");
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string GetMapModuleVersion()
    {
        try
        {
            string modulePath = GetMapModulePath();
            if (string.IsNullOrEmpty(modulePath))
            {
                return string.Empty;
            }

            return ReadSubModuleValue(modulePath, "Version");
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string GetMapModulePath()
    {
        try
        {
            object? mapSceneWrapper = Campaign.Current?.MapSceneWrapper;
            if (mapSceneWrapper == null)
            {
                return string.Empty;
            }

            // The concrete wrapper exposes a Scene instance (Sandbox.MapScene.Scene).
            var sceneProperty = mapSceneWrapper.GetType().GetProperty("Scene");
            if (sceneProperty?.GetValue(mapSceneWrapper) is not Scene scene)
            {
                return string.Empty;
            }
            if (!WNHelpers.TryGetBaseGameDirectoryPath(out string gamePath))
            {
                return string.Empty;
            }
            return scene.GetModulePath().Trim().TrimEnd('/', '\\').Replace("$BASE", gamePath);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string ReadSubModuleValue(string modulePath, string elementName)
    {
        string subModuleXmlPath = modulePath + "/SubModule.xml";
        if (!File.Exists(subModuleXmlPath))
        {
            return string.Empty;
        }

        XDocument document = XDocument.Load(subModuleXmlPath);
        XElement? element = document.Root?.Element(elementName);
        return element?.Attribute("value")?.Value ?? string.Empty;
    }
}

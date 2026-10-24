using System.ComponentModel;

using ImageCare.Core.Services.ConfigurationService;

using ImageCare.Mcp.Security;
using ImageCare.Mcp.Ui;

using ModelContextProtocol.Server;
using ModelContextProtocol;

namespace ImageCare.Mcp.Tools;

public sealed record DriveInfoResult(
    string Name,
    string RootPath,
    string DriveType,
    bool IsReady,
    long TotalBytes,
    long FreeBytes,
    string? VolumeLabel);

public sealed record FolderEntryResult(
    string Name,
    string Path,
    int MediaFiles,
    int Subfolders);

[McpServerToolType]
public sealed class SystemTools(
    IConfigurationService configurationService,
    McpPathGuard pathGuard,
    IMcpUiHost uiHost)
{
    [McpServerTool(
        Name = "imagecare_list_drives",
        Title = "List drives",
        ReadOnly = true,
        Idempotent = true,
        Destructive = false)]
    [Description(
        "Lists mounted drives with type, free space and readiness. Use this first to find a " +
        "removable card or flash drive before opening a folder on it.")]
    public string ListDrives()
    {
        var drives = new List<DriveInfoResult>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                drives.Add(new DriveInfoResult(
                    drive.Name,
                    drive.RootDirectory.FullName,
                    drive.DriveType.ToString(),
                    drive.IsReady,
                    drive.IsReady ? drive.TotalSize : 0,
                    drive.IsReady ? drive.AvailableFreeSpace : 0,
                    drive.IsReady ? TryGetLabel(drive) : null));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                drives.Add(new DriveInfoResult(
                    drive.Name,
                    drive.Name,
                    drive.DriveType.ToString(),
                    false,
                    0,
                    0,
                    null));
            }
        }

        return ToolSupport.Json(new
        {
            drives,
            uiAvailable = uiHost.IsAvailable
        });
    }

    [McpServerTool(
        Name = "imagecare_list_folders",
        Title = "List folders",
        ReadOnly = true,
        Idempotent = true,
        Destructive = false)]
    [Description(
        "Lists subfolders of an allowed directory with a count of supported media files in each. " +
        "Use it to walk a memory card (for example DCIM) before starting a review session.")]
    public string ListFolders(
        [Description("Absolute path to an allowed directory.")] string directoryPath,
        [Description("Include file counts; set false for a fast structural listing.")] bool includeMediaCounts = true,
        [Description("Maximum number of folders to return (1-500).")] int limit = 200)
    {
        var path = pathGuard.Require(directoryPath, nameof(directoryPath));
        if (!Directory.Exists(path))
        {
            throw new McpException($"Directory not found: {path}");
        }

        var effectiveLimit = Math.Clamp(limit, 1, 500);
        var entries = new List<FolderEntryResult>();

        foreach (var subfolder in Directory.EnumerateDirectories(path)
                     .OrderBy(folder => folder, StringComparer.OrdinalIgnoreCase)
                     .Take(effectiveLimit))
        {
            var mediaFiles = 0;
            if (includeMediaCounts)
            {
                try
                {
                    mediaFiles = Directory.EnumerateFiles(subfolder)
                        .Count(file => ImageCare.Core.Domain.MediaFormats.MediaFormat.IsSupportedExtension(
                            Path.GetExtension(file)));
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    mediaFiles = -1;
                }
            }

            entries.Add(new FolderEntryResult(
                Path.GetFileName(subfolder),
                subfolder,
                mediaFiles,
                Directory.EnumerateDirectories(subfolder).Count()));
        }

        return ToolSupport.Json(new
        {
            directory = path,
            folders = entries,
            truncated = entries.Count >= effectiveLimit
        });
    }

    [McpServerTool(
        Name = "imagecare_server_info",
        Title = "Server info",
        ReadOnly = true,
        Idempotent = true,
        Destructive = false)]
    [Description(
        "Reports MCP server capabilities, whether the desktop UI bridge is available, and which " +
        "folders are exposed as search roots.")]
    public string ServerInfo()
    {
        var configuration = configurationService.Configuration.Value;
        return ToolSupport.Json(new
        {
            server = "ImageCare.Mcp",
            uiAvailable = uiHost.IsAvailable,
            searchRoots = configuration.MediaSearchRootPaths,
            pinnedTags = configuration.PinnedTags,
            folderTagsEnabled = configuration.FolderTagsEnabled,
            safeDeletionEnabled = configuration.SafeDeletionEnabled
        });
    }

    private static string? TryGetLabel(DriveInfo drive)
    {
        try
        {
            return string.IsNullOrWhiteSpace(drive.VolumeLabel) ? null : drive.VolumeLabel;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}

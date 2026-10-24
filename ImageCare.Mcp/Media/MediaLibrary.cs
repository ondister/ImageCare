using ImageCare.Core.Domain.Folders;
using ImageCare.Core.Domain.MediaFormats;
using ImageCare.Core.Services.FileSystemService;
using ImageCare.Core.Services.FolderClusterizationService;
using ImageCare.Core.Services.FolderStatisticsService;

using Serilog;

namespace ImageCare.Mcp.Media;

/// <summary>
/// File-system facing helpers shared by the tools: media enumeration, format detection and
/// shooting-session aware batch packing.
/// </summary>
public sealed class MediaLibrary
{
    private readonly IFileSystemService _fileSystemService;
    private readonly ILogger _logger;

    public MediaLibrary(IFileSystemService fileSystemService, ILogger logger)
    {
        _fileSystemService = fileSystemService;
        _logger = logger;
    }

    public IReadOnlyList<string> EnumerateMedia(string directoryPath, bool recursive)
    {
        try
        {
            var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            return Directory.EnumerateFiles(directoryPath, "*", searchOption)
                .Where(path => MediaFormat.IsSupportedExtension(Path.GetExtension(path)))
                .Where(path => !Path.GetFileName(path).Equals(
                    "folder-tags.txt",
                    StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.Warning(exception, "Failed to enumerate media in {Directory}", directoryPath);
            return [];
        }
    }

    /// <summary>
    /// Splits a chronological list of files into batches that respect shooting clusters.
    /// Clusters are packed whole; a cluster larger than the batch size is chunked. This keeps
    /// bursts of frames from the same scene together, which is what makes culling fast.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<string>> PackBatches(
        IReadOnlyList<string> files,
        int batchSize,
        double epsilonMinutes = 2.0)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);

        if (files.Count == 0)
        {
            return [];
        }

        List<FilesCluster> clusters;
        try
        {
            var clusterFiles = files
                .Select(path => new ClusterFileModel(Path.GetFileName(path), path, GetCaptureTime(path)))
                .Where(file => file.CreatedDateTime.HasValue)
                .ToList();

            clusters = new DbScanClusteringService()
                .ClusterAutomatically(clusterFiles, minPoints: 2, epsilonMinutes)
                .Clusters
                .OrderBy(cluster => cluster.StartTime)
                .ToList();
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            _logger.Warning(exception, "Clusterization failed; falling back to plain time-ordered batching");
            clusters = files
                .Select((path, index) => (path, index))
                .OrderBy(item => GetCaptureTime(item.path) ?? DateTime.MinValue)
                .Select(item => new FilesCluster(item.index + 1, [
                    new FileModel(Path.GetFileName(item.path), item.path, GetCaptureTime(item.path))
                ]))
                .ToList();
        }

        var batches = new List<IReadOnlyList<string>>();
        var current = new List<string>(batchSize);

        void Flush()
        {
            if (current.Count == 0)
            {
                return;
            }

            batches.Add(current.ToArray());
            current.Clear();
        }

        foreach (var cluster in clusters)
        {
            var clusterItems = cluster.Files.Values
                .OrderBy(file => file.CreatedDateTime)
                .ThenBy(file => file.FullName, StringComparer.OrdinalIgnoreCase)
                .Select(file => file.FullName)
                .ToList();

            if (clusterItems.Count > batchSize)
            {
                Flush();
                for (var offset = 0; offset < clusterItems.Count; offset += batchSize)
                {
                    batches.Add(clusterItems
                        .Skip(offset)
                        .Take(batchSize)
                        .ToArray());
                }

                continue;
            }

            if (current.Count + clusterItems.Count > batchSize)
            {
                Flush();
            }

            current.AddRange(clusterItems);
        }

        Flush();
        return batches;
    }

    public static DateTime? GetCaptureTime(string path)
    {
        try
        {
            var fileInfo = new FileInfo(path);
            if (!fileInfo.Exists)
            {
                return null;
            }

            // Camera files rarely update after capture, so the earlier of creation and last
            // write time is a good approximation of the shutter timestamp without decoding EXIF.
            return fileInfo.CreationTimeUtc < fileInfo.LastWriteTimeUtc
                ? fileInfo.CreationTimeUtc.ToLocalTime()
                : fileInfo.LastWriteTimeUtc.ToLocalTime();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public FileInfo GetFileInfo(string path) => _fileSystemService.GetFileInfo(path);

    public bool FileExists(string path) => _fileSystemService.FileExists(path);
}

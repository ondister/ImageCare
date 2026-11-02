using System.Text;
using System.Text.Json;

using ImageCare.Core.Domain.Configuration;

namespace ImageCare.Core.Services.MediaPreviewOperationsService;

internal interface IDeletionManifestStore
{
    IReadOnlyList<DeletedMediaFile> Load();

    void Save(IReadOnlyList<DeletedMediaFile> deletedFiles);
}

internal sealed class DeletionManifestStore : IDeletionManifestStore
{
    internal const string ManifestFileName = "deletion-manifest.json";

    private readonly Func<Configuration> _getConfiguration;

    public DeletionManifestStore(Func<Configuration> getConfiguration)
    {
        _getConfiguration = getConfiguration ?? throw new ArgumentNullException(nameof(getConfiguration));
    }

    public IReadOnlyList<DeletedMediaFile> Load()
    {
        var manifestPath = GetManifestPath();
        if (!File.Exists(manifestPath))
        {
            return Array.Empty<DeletedMediaFile>();
        }

        try
        {
            using var stream = new FileStream(
                manifestPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);
            var deletedFiles = JsonSerializer.Deserialize(
                stream,
                typeof(List<DeletedMediaFile>),
                ConfigurationSourceGenerationContext.Default) as List<DeletedMediaFile>;
            return deletedFiles ?? new List<DeletedMediaFile>();
        }
        catch
        {
            // A damaged manifest or temporarily unavailable path must not prevent startup.
            // Leave the original file untouched so recovery can be retried later.
            return Array.Empty<DeletedMediaFile>();
        }
    }

    public void Save(IReadOnlyList<DeletedMediaFile> deletedFiles)
    {
        var manifestPath = GetManifestPath();
        var directory = Path.GetDirectoryName(manifestPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        Directory.CreateDirectory(directory);
        var temporaryPath = manifestPath + ".tmp";
        try
        {
            var serializableFiles = deletedFiles as List<DeletedMediaFile>
                ?? deletedFiles.ToList();
            var content = JsonSerializer.Serialize(
                serializableFiles,
                typeof(List<DeletedMediaFile>),
                ConfigurationSourceGenerationContext.Default);
            var bytes = Encoding.UTF8.GetBytes(content);
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.Create,
                       FileAccess.Write,
                       FileShare.None,
                       16 * 1024,
                       FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, manifestPath, overwrite: true);
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch
            {
                // The manifest replace already determines success. Temporary-file cleanup is
                // best effort and a later save will overwrite the same path.
            }
        }
    }

    internal string GetManifestPath()
    {
        var bufferDirectory = _getConfiguration().SafeDeletionDirectoryPath;
        if (string.IsNullOrWhiteSpace(bufferDirectory))
        {
            bufferDirectory = Configuration.GetDefaultSafeDeletionDirectoryPath();
        }

        return Path.Combine(bufferDirectory, ManifestFileName);
    }
}

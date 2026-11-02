using System.Xml.Linq;
using ImageCare.Core.Domain.Annotations;
using ImageCare.Core.Services.FileSystemService;

namespace ImageCare.Core.Services.MediaAnnotationsService;

public sealed class XmpMediaAnnotationsStorageService : IMediaAnnotationsStorageService
{
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private readonly XmpDocumentCodec _codec;
    private readonly SidecarXmpStorage _sidecarStorage;
    private readonly EmbeddedJpegXmpStorage _embeddedStorage;

    public XmpMediaAnnotationsStorageService(IFileSystemService fileSystemService)
    {
        ArgumentNullException.ThrowIfNull(fileSystemService);
        _codec = new XmpDocumentCodec();
        _sidecarStorage = new SidecarXmpStorage(_codec);
        _embeddedStorage = new EmbeddedJpegXmpStorage(_codec, fileSystemService);
    }

    public async Task<MediaAnnotationsSnapshot> ReadAsync(
        string mediaPath,
        CancellationToken cancellationToken = default)
    {
        var fullMediaPath = NormalizeExistingMediaPath(mediaPath);
        XmpSidecarPath.EnsureUnambiguousRawOwner(fullMediaPath);
        var storagePath = XmpSidecarPath.GetExternalSidecarPath(fullMediaPath) ?? fullMediaPath;
        var document = await ReadDocumentAsync(fullMediaPath, storagePath, cancellationToken)
            .ConfigureAwait(false);

        return new MediaAnnotationsSnapshot(
            fullMediaPath,
            document is null ? MediaAnnotations.Empty : _codec.ReadAnnotations(document),
            CaptureState(fullMediaPath, storagePath));
    }

    public async Task<MediaAnnotationsSnapshot> WriteAsync(
        string mediaPath,
        MediaAnnotations annotations,
        AnnotationFileState? expectedState = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(annotations);

        var fullMediaPath = NormalizeExistingMediaPath(mediaPath);
        XmpSidecarPath.EnsureUnambiguousRawOwner(fullMediaPath);
        var storagePath = XmpSidecarPath.GetExternalSidecarPath(fullMediaPath) ?? fullMediaPath;
        var initialState = CaptureState(fullMediaPath, storagePath);
        if (expectedState is not null)
        {
            EnsureUnchanged(expectedState, initialState, fullMediaPath);
        }

        void EnsureReadyToCommit() =>
            EnsureUnchanged(initialState, CaptureState(fullMediaPath, storagePath), fullMediaPath);

        if (XmpSidecarPath.UsesEmbeddedXmp(fullMediaPath))
        {
            await _embeddedStorage.WriteAsync(
                    fullMediaPath,
                    annotations,
                    EnsureReadyToCommit,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            await _sidecarStorage.WriteAsync(
                    storagePath,
                    annotations,
                    EnsureReadyToCommit,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return await ReadAsync(fullMediaPath, CancellationToken.None).ConfigureAwait(false);
    }

    private Task<XDocument?> ReadDocumentAsync(
        string mediaPath,
        string storagePath,
        CancellationToken cancellationToken) =>
        XmpSidecarPath.UsesEmbeddedXmp(mediaPath)
            ? _embeddedStorage.ReadAsync(mediaPath, cancellationToken)
            : _sidecarStorage.ReadAsync(storagePath, cancellationToken);

    private static AnnotationFileState CaptureState(string mediaPath, string storagePath)
    {
        var media = new FileInfo(mediaPath);
        media.Refresh();
        if (!media.Exists)
        {
            throw new FileNotFoundException("Media file was not found.", mediaPath);
        }

        var storage = new FileInfo(storagePath);
        storage.Refresh();
        return new AnnotationFileState(
            storagePath,
            storage.Exists,
            storage.Exists ? storage.Length : 0,
            storage.Exists ? storage.LastWriteTimeUtc : DateTime.MinValue,
            media.Length,
            media.LastWriteTimeUtc);
    }

    private static void EnsureUnchanged(
        AnnotationFileState expected,
        AnnotationFileState actual,
        string mediaPath)
    {
        var unchanged = PathComparer.Equals(expected.StoragePath, actual.StoragePath)
            && expected.StorageExists == actual.StorageExists
            && expected.StorageLength == actual.StorageLength
            && expected.StorageModifiedUtc == actual.StorageModifiedUtc
            && expected.MediaLength == actual.MediaLength
            && expected.MediaModifiedUtc == actual.MediaModifiedUtc;
        if (!unchanged)
        {
            throw new MediaAnnotationsConcurrentModificationException(mediaPath);
        }
    }

    private static string NormalizeExistingMediaPath(string mediaPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaPath);
        var fullPath = Path.GetFullPath(mediaPath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Media file was not found.", fullPath);
        }

        return fullPath;
    }
}

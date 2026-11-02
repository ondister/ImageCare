using System.Xml.Linq;
using ImageCare.Core.Domain.Annotations;

namespace ImageCare.Core.Services.MediaAnnotationsService;

internal sealed class SidecarXmpStorage(XmpDocumentCodec codec)
{
    public async Task<XDocument?> ReadAsync(string storagePath, CancellationToken cancellationToken)
    {
        if (!File.Exists(storagePath))
        {
            return null;
        }

        await using var input = new FileStream(
            storagePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await codec.LoadAsync(input, cancellationToken).ConfigureAwait(false);
    }

    public async Task WriteAsync(
        string storagePath,
        MediaAnnotations annotations,
        Action ensureReadyToCommit,
        CancellationToken cancellationToken)
    {
        var document = await ReadAsync(storagePath, cancellationToken).ConfigureAwait(false)
            ?? codec.CreateDocument();
        codec.WriteAnnotations(document, annotations);
        var content = codec.Serialize(document);

        var directory = Path.GetDirectoryName(storagePath)
            ?? throw new InvalidOperationException($"Cannot determine a directory for {storagePath}.");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(storagePath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var output = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await output.WriteAsync(content, cancellationToken).ConfigureAwait(false);
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            ensureReadyToCommit();
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, storagePath, true);
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // Cleanup must not hide the original write failure.
        }
    }
}

using System.Text;

using ImageCare.Core.Services.FileSystemService;

namespace ImageCare.Core.Domain.Annotations;

public sealed class FolderTagsStore : IFolderTagsStore
{
    private readonly IFileSystemService _fileSystemService;

    public FolderTagsStore(IFileSystemService fileSystemService)
    {
        _fileSystemService = fileSystemService ?? throw new ArgumentNullException(nameof(fileSystemService));
    }

    public string GetDescriptorPath(string directoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        return Path.Combine(Path.GetFullPath(directoryPath), FolderTagsFile.FileName);
    }

    public bool HasDescriptor(string directoryPath)
    {
        try
        {
            return _fileSystemService.FileExists(GetDescriptorPath(directoryPath));
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public IReadOnlyList<string>? Read(string directoryPath)
    {
        var descriptorPath = GetDescriptorPath(directoryPath);
        if (!_fileSystemService.FileExists(descriptorPath))
        {
            return null;
        }

        try
        {
            using var stream = _fileSystemService.OpenRead(descriptorPath);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return FolderTagsFile.Parse(reader.ReadToEnd());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public async Task WriteAsync(
        string directoryPath,
        IEnumerable<string> tags,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        var fullPath = Path.GetFullPath(directoryPath);
        var descriptorPath = GetDescriptorPath(fullPath);
        var content = FolderTagsFile.Serialize(tags);

        if (content.Length == 0)
        {
            if (_fileSystemService.FileExists(descriptorPath))
            {
                _fileSystemService.DeleteFile(descriptorPath);
            }

            return;
        }

        _fileSystemService.CreateDirectory(fullPath);
        var temporaryPath = Path.Combine(fullPath, $".{FolderTagsFile.FileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var output = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                16 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
                await output.WriteAsync(encoding.GetPreamble(), cancellationToken).ConfigureAwait(false);
                var bytes = encoding.GetBytes(content);
                await output.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, descriptorPath, true);
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

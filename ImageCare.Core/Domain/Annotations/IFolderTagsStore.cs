namespace ImageCare.Core.Domain.Annotations;

/// <summary>
/// Reads and writes <c>folder-tags.txt</c> descriptors.
/// </summary>
public interface IFolderTagsStore
{
    string GetDescriptorPath(string directoryPath);

    bool HasDescriptor(string directoryPath);

    /// <summary>
    /// Reads the descriptor for a directory. Returns <see langword="null"/> when no
    /// descriptor file exists, and an empty list when the file exists but has no tags.
    /// </summary>
    IReadOnlyList<string>? Read(string directoryPath);

    /// <summary>
    /// Writes the descriptor atomically. An empty tag set removes the descriptor file.
    /// </summary>
    Task WriteAsync(string directoryPath, IEnumerable<string> tags, CancellationToken cancellationToken = default);
}

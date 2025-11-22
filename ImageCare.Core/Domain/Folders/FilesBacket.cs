using System.Collections.Concurrent;

namespace ImageCare.Core.Domain.Folders;

public record FilesBucket(
    DateTime Date,
    int FilesCount,
    ConcurrentDictionary<string,FileModel> Files);
using System.Collections.ObjectModel;

namespace ImageCare.Core.Domain;

public class DirectoryModel
{
    private readonly List<DirectoryModel> _directories = [];
    private readonly List<FileModel> _files = [];

    public DirectoryModel(string? name, string path)
    {
        Name = name;
        Path = path;

        DirectoryModels = new ReadOnlyCollection<DirectoryModel>(_directories);
        FileModels = new ReadOnlyCollection<FileModel>(_files);
    }

    public string? Name { get; }

    public string Path { get; }

    public IReadOnlyCollection<DirectoryModel> DirectoryModels { get; }

    public IReadOnlyCollection<FileModel> FileModels { get; }

    public void AddFile(FileModel fileModel)
    {
        _files.Add(fileModel);
    }

    internal void AddDirectory(DirectoryModel directoryModel)
    {
        _directories.Add(directoryModel);
    }

    internal void AddDirectories(IEnumerable<DirectoryModel> directoryModels)
    {
        _directories.AddRange(directoryModels);
    }
}
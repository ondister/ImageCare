using System.Collections.ObjectModel;

namespace ImageCare.Core.Domain;

public class DirectoryModel
{
    private readonly List<DirectoryModel> _directories = [];

    public DirectoryModel(string? name, string path)
    {
        Name = name;
        Path = path;

        DirectoryModels = new ReadOnlyCollection<DirectoryModel>(_directories);
    }

    public static DirectoryModel Empty { get; } = new(string.Empty, string.Empty);

    public string? Name { get; }

    public string Path { get; }

    public IReadOnlyCollection<DirectoryModel> DirectoryModels { get; }

    public void AddDirectory(DirectoryModel directoryModel)
    {
        _directories.Add(directoryModel);
    }

    public void AddDirectories(IEnumerable<DirectoryModel> directoryModels)
    {
        _directories.AddRange(directoryModels);
    }

    public DirectoryModel? GetParent()
    {
        if (this == Empty)
        {
            return null;
        }

        var directoryInfo = new DirectoryInfo(Path);

        if (directoryInfo.Parent == null)
        {
            return null;
        }

        return new DirectoryModel(directoryInfo.Parent.Name, directoryInfo.Parent.FullName);
    }
}
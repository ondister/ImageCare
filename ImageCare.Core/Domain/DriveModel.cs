
namespace ImageCare.Core.Domain;

public abstract class DriveModel(string name, string path) : DirectoryModel(name, path)
{
    public DirectoryModel? RootDirectory { get; set; }
}
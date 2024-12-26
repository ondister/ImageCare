
namespace ImageCare.Core.Domain;

public class DriveModel(string name, string path) : DirectoryModel(name, path)
{
    public DirectoryModel? RootDirectory { get; set; }
}
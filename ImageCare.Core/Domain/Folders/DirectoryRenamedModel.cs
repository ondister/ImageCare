namespace ImageCare.Core.Domain.Folders;

public sealed class DirectoryRenamedModel
{
    public DirectoryRenamedModel(DirectoryModel oldDirectoryModel, DirectoryModel newDirectoryModel)
    {
        OldDirectoryModel = oldDirectoryModel;
        NewDirectoryModel = newDirectoryModel;
    }

    public DirectoryModel OldDirectoryModel { get; }

    public DirectoryModel NewDirectoryModel { get; }
}
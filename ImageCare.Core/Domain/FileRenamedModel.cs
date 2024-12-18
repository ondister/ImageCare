namespace ImageCare.Core.Domain;

public sealed class FileRenamedModel
{
    public FileRenamedModel(FileModel oldFileModel, FileModel newFileModel)
    {
        OldFileModel = oldFileModel;
        NewFileModel = newFileModel;
    }

    public FileModel OldFileModel { get; }

    public FileModel NewFileModel { get; }
}
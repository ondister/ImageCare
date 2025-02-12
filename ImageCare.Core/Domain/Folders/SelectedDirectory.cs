namespace ImageCare.Core.Domain.Folders;

public class SelectedDirectory : DirectoryModel
{
    /// <inheritdoc />
    public SelectedDirectory(string? name, string path, FileManagerPanel fileManagerPanel)
        : base(name, path)
    {
        FileManagerPanel = fileManagerPanel;
    }

    public SelectedDirectory(DirectoryModel directoryModel, FileManagerPanel fileManagerPanel)
        : this(directoryModel.Name, directoryModel.Path, fileManagerPanel) { }

    public FileManagerPanel FileManagerPanel { get; }
}
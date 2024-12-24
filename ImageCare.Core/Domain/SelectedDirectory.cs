namespace ImageCare.Core.Domain;

public class SelectedDirectory : DirectoryModel
{
    /// <inheritdoc />
    public SelectedDirectory(string? name, string path, FileManagerPanel fileManagerPanel)
        : base(name, path)
    {
        FileManagerPanel = fileManagerPanel;
    }

    public FileManagerPanel FileManagerPanel { get; }
}
using ImageCare.Core.Domain.MediaFormats;

namespace ImageCare.Core.Domain;

public class SelectedImagePreview : ImagePreview
{
    /// <inheritdoc />
    public SelectedImagePreview(string? title, string url, MediaFormat mediaFormat, FileManagerPanel fileManagerPanel)
        : base(title, url, mediaFormat)
    {
        FileManagerPanel = fileManagerPanel;
    }

    public FileManagerPanel FileManagerPanel { get; }
}
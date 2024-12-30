using ImageCare.Core.Domain.MediaFormats;

namespace ImageCare.Core.Domain;

public class SelectedImagePreview : ImagePreview
{
    /// <inheritdoc />
    public SelectedImagePreview(string? title, string url, MediaFormat mediaFormat, int maxImageHeight, FileManagerPanel fileManagerPanel)
        : base(title, url, mediaFormat, maxImageHeight)
    {
        FileManagerPanel = fileManagerPanel;
    }

    public SelectedImagePreview(ImagePreview imagePreview, FileManagerPanel fileManagerPanel)
        : this(imagePreview.Title, imagePreview.Url, imagePreview.MediaFormat, imagePreview.MaxImageHeight, fileManagerPanel) { }

    public FileManagerPanel FileManagerPanel { get; }
}
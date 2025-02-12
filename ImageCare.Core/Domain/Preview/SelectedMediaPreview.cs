using ImageCare.Core.Domain.Folders;
using ImageCare.Core.Domain.MediaFormats;

namespace ImageCare.Core.Domain.Preview;

public class SelectedMediaPreview : MediaPreview
{
    /// <inheritdoc />
    public SelectedMediaPreview(string? title, string url, MediaFormat mediaFormat, int maxImageHeight, FileManagerPanel fileManagerPanel)
        : base(title, url, mediaFormat, maxImageHeight)
    {
        FileManagerPanel = fileManagerPanel;
    }

    public SelectedMediaPreview(MediaPreview imagePreview, FileManagerPanel fileManagerPanel)
        : this(imagePreview.Title, imagePreview.Url, imagePreview.MediaFormat, imagePreview.MaxImageHeight, fileManagerPanel) { }

    public FileManagerPanel FileManagerPanel { get; }
}
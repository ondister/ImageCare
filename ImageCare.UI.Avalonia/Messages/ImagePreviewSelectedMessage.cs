using CommunityToolkit.Mvvm.Messaging.Messages;

using ImageCare.Core.Domain;

namespace ImageCare.UI.Avalonia.Messages;

internal class ImagePreviewSelectedMessage : ValueChangedMessage<ImagePreview>
{
    /// <inheritdoc />
    public ImagePreviewSelectedMessage(ImagePreview value)
        : base(value) { }

    /// <inheritdoc />
    public ImagePreviewSelectedMessage(ImagePreview value, string mode)
        : this(value)
    {
        Mode = mode;
    }

    public string Mode { get; }
}
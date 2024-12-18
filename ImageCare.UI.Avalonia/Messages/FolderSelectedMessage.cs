using CommunityToolkit.Mvvm.Messaging.Messages;

using ImageCare.Core.Domain;

namespace ImageCare.UI.Avalonia.Messages;

internal class FolderSelectedMessage : ValueChangedMessage<DirectoryModel>
{
    /// <inheritdoc />
    public FolderSelectedMessage(DirectoryModel value)
        : base(value) { }

    /// <inheritdoc />
    public FolderSelectedMessage(DirectoryModel value, string mode)
        : this(value)
    {
        Mode = mode;
    }

    public string Mode { get; }
}
namespace ImageCare.Core.Services.MediaPreviewOperationsService;

public enum MediaFileOperationKind
{
    Copy,
    Move,
    Delete
}

public sealed record MediaFileOperationCompleted(
    MediaFileOperationKind Kind,
    string SourcePath,
    string? DestinationPath = null);

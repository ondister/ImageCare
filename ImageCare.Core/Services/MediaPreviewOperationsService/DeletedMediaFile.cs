namespace ImageCare.Core.Services.MediaPreviewOperationsService;

public sealed record DeletedMediaFile(
    string OriginalPath,
    string BufferPath,
    DateTime DeletedAtUtc);

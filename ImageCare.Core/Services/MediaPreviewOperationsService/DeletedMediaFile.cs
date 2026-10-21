namespace ImageCare.Core.Services.MediaPreviewOperationsService;

public enum DeletedMediaFileState
{
    // Keep Committed as zero so manifests written before journal phases were introduced
    // deserialize as completed safe deletes.
    Committed = 0,
    PendingDelete = 1,
    PendingRestore = 2,
    CleanupPending = 3
}

public sealed record DeletedMediaFile(
    string OriginalPath,
    string BufferPath,
    DateTime DeletedAtUtc,
    string? OriginalSidecarPath = null,
    string? BufferSidecarPath = null,
    DeletedMediaFileState State = DeletedMediaFileState.Committed,
    string? RestorePath = null,
    string? RestoreSidecarPath = null);

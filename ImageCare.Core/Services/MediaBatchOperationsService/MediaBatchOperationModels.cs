namespace ImageCare.Core.Services.MediaBatchOperationsService;

public enum MediaBatchOperationKind
{
    Copy,
    Move,
    Delete,
    ExportJpeg
}

public sealed record MediaBatchOperationProgress(
    MediaBatchOperationKind Kind,
    int Processed,
    int Total,
    string CurrentPath);

public sealed record MediaBatchOperationFailure(string SourcePath, string Error);

public sealed record MediaBatchOperationSummary(
    MediaBatchOperationKind Kind,
    int Total,
    int Succeeded,
    IReadOnlyList<MediaBatchOperationFailure> Failures,
    bool Cancelled,
    IReadOnlyList<string> DestinationPaths)
{
    public int Failed => Failures.Count;
}

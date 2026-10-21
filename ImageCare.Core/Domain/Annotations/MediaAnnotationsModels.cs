namespace ImageCare.Core.Domain.Annotations;

public sealed record AnnotationFileState(
    string StoragePath,
    bool StorageExists,
    long StorageLength,
    DateTime StorageModifiedUtc,
    long MediaLength,
    DateTime MediaModifiedUtc);

public sealed record MediaAnnotationsSnapshot(
    string MediaPath,
    MediaAnnotations Annotations,
    AnnotationFileState FileState);

public sealed record IndexedMediaAnnotations(
    string MediaPath,
    MediaAnnotations Annotations,
    long MediaLength,
    DateTime MediaModifiedUtc,
    long AnnotationStorageLength,
    DateTime AnnotationModifiedUtc,
    DateTime IndexedUtc);

public sealed record MediaAnnotationsSearchQuery(
    string? Text = null,
    string? RootPath = null,
    int? MinimumRating = null,
    int? MaximumRating = null,
    IReadOnlyCollection<string>? Tags = null,
    bool MatchAllTags = true,
    int Offset = 0,
    int Limit = 200);

public enum AnnotationsReconciliationState
{
    Idle,
    Running,
    Completed,
    Cancelled,
    Failed
}

public sealed record AnnotationsReconciliationStatus(
    AnnotationsReconciliationState State,
    string? CurrentPath,
    int ProcessedFiles,
    string? Error = null)
{
    public static AnnotationsReconciliationStatus Idle { get; } =
        new(AnnotationsReconciliationState.Idle, null, 0);
}

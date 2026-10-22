namespace ImageCare.Core.Domain.Annotations;

public enum FolderTagApplicationState
{
    Idle,
    Running,
    Completed,
    Cancelled,
    Failed
}

public sealed record FolderTagApplicationStatus(
    FolderTagApplicationState State,
    string? CurrentPath,
    int ProcessedFiles,
    int TotalFiles,
    int UpdatedFiles,
    int SkippedFiles,
    int FailedFiles,
    string? Error = null)
{
    public static FolderTagApplicationStatus Idle { get; } = new(
        FolderTagApplicationState.Idle,
        null,
        0,
        0,
        0,
        0,
        0);

    public bool IsRunning => State == FolderTagApplicationState.Running;

    public double Progress => TotalFiles == 0 ? 0 : (double)ProcessedFiles / TotalFiles;
}

public sealed record FolderTagApplicationResult(
    string FilePath,
    FolderTagApplicationOutcome Outcome,
    IReadOnlyList<string> AddedTags);

public enum FolderTagApplicationOutcome
{
    /// <summary>The file already contained every folder tag; nothing was written.</summary>
    Skipped,

    /// <summary>New tags were merged into the file's annotations.</summary>
    Updated,

    /// <summary>The file could not be processed; the operation continued.</summary>
    Failed
}

public interface IFolderTagApplicationService : IAsyncDisposable
{
    IObservable<FolderTagApplicationStatus> ApplicationStatus { get; }

    /// <summary>
    /// Applies the effective folder-tag set of the file's directory to a single media
    /// file. Idempotent: when the file already contains every tag, nothing is written.
    /// </summary>
    Task<FolderTagApplicationResult> ApplyToFileAsync(
        string mediaFilePath,
        string action,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies the effective folder-tag set of <paramref name="directoryPath"/> to every
    /// supported media file directly inside it (non-recursive). Bounded parallelism with
    /// progress reporting and cancellation; individual failures never abort the run.
    /// </summary>
    Task ApplyToDirectoryAsync(
        string directoryPath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves the effective tag set that would be applied to media inside the directory.
    /// </summary>
    IReadOnlyList<string> GetEffectiveTags(string directoryPath);
}

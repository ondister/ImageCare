using ImageCare.Core.Domain.Annotations;

namespace ImageCare.Core.Services.MediaAnnotationsService;

public interface IMediaAnnotationsService : IAsyncDisposable
{
    IObservable<AnnotationsReconciliationStatus> ReconciliationStatus { get; }

    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task<MediaAnnotations> GetAsync(string mediaPath, CancellationToken cancellationToken = default);

    Task<MediaAnnotationsSnapshot> GetSnapshotAsync(
        string mediaPath,
        CancellationToken cancellationToken = default);

    Task<MediaAnnotationsSnapshot> SaveAsync(
        string mediaPath,
        MediaAnnotations annotations,
        AnnotationFileState? expectedState = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<IndexedMediaAnnotations>> GetDirectoryAsync(
        string directoryPath,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<IndexedMediaAnnotations>> SearchAsync(
        MediaAnnotationsSearchQuery query,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetAllTagsAsync(CancellationToken cancellationToken = default);

    Task RegisterDirectoryAsync(string directoryPath, CancellationToken cancellationToken = default);

    Task RefreshDirectoryAsync(string directoryPath, CancellationToken cancellationToken = default);

    Task RegisterRootsAsync(IEnumerable<string> roots, CancellationToken cancellationToken = default);

    Task ReconcileRegisteredRootsAsync(CancellationToken cancellationToken = default);

    Task StartBackgroundReconciliationAsync(
        TimeSpan interval,
        CancellationToken cancellationToken = default);

    Task StopBackgroundReconciliationAsync(CancellationToken cancellationToken = default);
}

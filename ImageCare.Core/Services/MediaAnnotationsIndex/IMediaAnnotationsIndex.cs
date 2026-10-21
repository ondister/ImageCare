using ImageCare.Core.Domain.Annotations;

namespace ImageCare.Core.Services.MediaAnnotationsIndex;

public interface IMediaAnnotationsIndex
{
    string DatabasePath { get; }

    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task UpsertAsync(IndexedMediaAnnotations media, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<IndexedMediaAnnotations>> GetDirectoryAsync(
        string directoryPath,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<IndexedMediaAnnotations>> SearchAsync(
        MediaAnnotationsSearchQuery query,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetAllTagsAsync(CancellationToken cancellationToken = default);

    Task RemoveAsync(string mediaPath, CancellationToken cancellationToken = default);

    Task MoveAsync(string oldMediaPath, string newMediaPath, CancellationToken cancellationToken = default);

    Task ReconcileDirectoryAsync(
        string directoryPath,
        CancellationToken cancellationToken = default);

    Task RebuildAsync(
        IEnumerable<string> roots,
        IProgress<AnnotationsReconciliationStatus>? progress = null,
        CancellationToken cancellationToken = default);
}

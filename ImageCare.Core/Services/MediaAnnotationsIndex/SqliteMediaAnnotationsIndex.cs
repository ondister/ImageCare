using ImageCare.Core.Domain.Annotations;
using ImageCare.Core.Services.MediaAnnotationsService;

namespace ImageCare.Core.Services.MediaAnnotationsIndex;

public sealed class SqliteMediaAnnotationsIndex : IMediaAnnotationsIndex
{
    private readonly SqliteMediaAnnotationsDatabase _database;
    private readonly SqliteMediaAnnotationsQueries _queries;
    private readonly MediaAnnotationsIndexReconciler _reconciler;

    public SqliteMediaAnnotationsIndex(IMediaAnnotationsStorageService storage, string? databasePath = null)
    {
        ArgumentNullException.ThrowIfNull(storage);
        _database = new SqliteMediaAnnotationsDatabase(databasePath);
        _queries = new SqliteMediaAnnotationsQueries(_database);
        _reconciler = new MediaAnnotationsIndexReconciler(storage, _database, _queries);
    }

    public string DatabasePath => _database.DatabasePath;

    public Task InitializeAsync(CancellationToken cancellationToken = default) =>
        _database.InitializeAsync(cancellationToken);

    public Task UpsertAsync(
        IndexedMediaAnnotations media,
        CancellationToken cancellationToken = default) =>
        _queries.UpsertAsync(media, cancellationToken);

    public Task<IReadOnlyList<IndexedMediaAnnotations>> GetDirectoryAsync(
        string directoryPath,
        CancellationToken cancellationToken = default) =>
        _queries.GetDirectoryAsync(directoryPath, cancellationToken);

    public Task<IReadOnlyList<IndexedMediaAnnotations>> SearchAsync(
        MediaAnnotationsSearchQuery query,
        CancellationToken cancellationToken = default) =>
        _queries.SearchAsync(query, cancellationToken);

    public Task<IReadOnlyList<string>> GetAllTagsAsync(CancellationToken cancellationToken = default) =>
        _queries.GetAllTagsAsync(cancellationToken);

    public Task RemoveAsync(string mediaPath, CancellationToken cancellationToken = default) =>
        _queries.RemoveAsync(mediaPath, cancellationToken);

    public Task MoveAsync(
        string oldMediaPath,
        string newMediaPath,
        CancellationToken cancellationToken = default) =>
        _queries.MoveAsync(oldMediaPath, newMediaPath, cancellationToken);

    public Task ReconcileDirectoryAsync(
        string directoryPath,
        CancellationToken cancellationToken = default) =>
        _reconciler.ReconcileDirectoryAsync(directoryPath, cancellationToken);

    public Task RebuildAsync(
        IEnumerable<string> roots,
        IProgress<AnnotationsReconciliationStatus>? progress = null,
        CancellationToken cancellationToken = default,
        bool force = false) =>
        _reconciler.RebuildAsync(roots, progress, cancellationToken, force);
}

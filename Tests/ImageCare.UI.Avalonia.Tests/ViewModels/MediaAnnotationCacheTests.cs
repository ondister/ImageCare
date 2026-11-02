using System;
using System.Collections.Generic;
using System.IO;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;

using ImageCare.Core.Domain;
using ImageCare.Core.Domain.Annotations;
using ImageCare.Core.Domain.Folders;
using ImageCare.Core.Domain.Media;
using ImageCare.Core.Domain.Media.Metadata;
using ImageCare.Core.Domain.MediaFormats;
using ImageCare.Core.Domain.Notification;
using ImageCare.Core.Domain.Preview;
using ImageCare.Core.Services.FileAssociationsService;
using ImageCare.Core.Services.MediaAnnotationsService;
using ImageCare.Core.Services.MediaPreviewOperationsService;
using ImageCare.Core.Services.MediaPreviewService;
using ImageCare.Core.Services.NotificationService;
using ImageCare.UI.Avalonia.Localization;
using ImageCare.UI.Avalonia.ViewModels;
using ImageCare.UI.Avalonia.ViewModels.Domain;

using MapsterMapper;

using Serilog;

namespace ImageCare.UI.Avalonia.Tests.ViewModels;

[TestFixture]
public sealed class MediaAnnotationCacheTests
{
    private string _directory = null!;
    private FakeAnnotationsService _annotations = null!;
    private MediaAnnotationCache _cache = null!;
    private List<MediaPreviewViewModel> _previews = null!;
    private MediaPreviewViewModel? _selectedPreview;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "ImageCareAnnotationCache", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _annotations = new FakeAnnotationsService();
        _previews = [];
        _cache = new MediaAnnotationCache(
            _annotations,
            LocalizationManager.Instance,
            new LoggerConfiguration().CreateLogger(),
            new ImmediateSynchronizationContext());
        _cache.SetSources(
            () => _previews,
            () => _directory,
            () => _selectedPreview,
            () => CancellationToken.None);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _annotations.DisposeAsync();
        Directory.Delete(_directory, recursive: true);
    }

    [Test]
    public async Task LoadCachedDirectoryAsync_AppliesIndexedAnnotationsAndRaisesEvent()
    {
        var path = CreateMediaFile("one.jpg");
        var preview = CreatePreview(path, MediaFormat.MediaFormatJpg);
        _previews.Add(preview);
        _annotations.DirectoryEntries = [CreateIndexed(path, rating: 4, tags: ["bird"] )];
        var applied = 0;
        _cache.AnnotationsApplied += (_, _) => applied++;

        await _cache.LoadCachedDirectoryAsync(_directory, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(preview.Rating, Is.EqualTo(4));
            Assert.That(preview.Tags, Is.EqualTo(new[] { "bird" }));
            Assert.That(preview.AnnotationState, Is.EqualTo(LocalizationManager.Instance["Annotations_StateCached"]));
            Assert.That(applied, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task LoadCachedDirectoryAsync_WhenFolderChanges_DoesNotApplyStaleResult()
    {
        var path = CreateMediaFile("one.jpg");
        var preview = CreatePreview(path, MediaFormat.MediaFormatJpg);
        _previews.Add(preview);
        _annotations.DirectoryEntries = [CreateIndexed(path, rating: 5, tags: [])];
        _cache.SetSources(() => _previews, () => Path.Combine(_directory, "other"));

        await _cache.LoadCachedDirectoryAsync(_directory, CancellationToken.None);

        Assert.That(preview.Rating, Is.Zero);
    }

    [Test]
    public void ApplyCached_PreservesAuthoritativeSnapshotForSelectedPreview()
    {
        var path = CreateMediaFile("selected.jpg");
        var preview = CreatePreview(path, MediaFormat.MediaFormatJpg);
        var snapshot = CreateSnapshot(path, rating: 3, tags: ["current"]);
        preview.ApplyAnnotations(snapshot.Annotations, "initial", snapshot);
        _selectedPreview = preview;
        _previews.Add(preview);

        _cache.ApplyCached(preview);

        Assert.Multiple(() =>
        {
            Assert.That(preview.Rating, Is.EqualTo(3));
            Assert.That(preview.Tags, Is.EqualTo(new[] { "current" }));
            Assert.That(preview.AnnotationSnapshot, Is.SameAs(snapshot));
            Assert.That(preview.AnnotationState, Is.EqualTo(LocalizationManager.Instance["Annotations_StateCurrent"]));
        });
    }

    [Test]
    public async Task RefreshChangedAsync_AppliesFreshSnapshotAndRaisesEvent()
    {
        var path = CreateMediaFile("changed.jpg");
        var preview = CreatePreview(path, MediaFormat.MediaFormatJpg);
        _previews.Add(preview);
        _annotations.Snapshots[path] = CreateSnapshot(path, rating: 2, tags: ["fresh"]);
        MediaPreviewViewModel? changed = null;
        _cache.ChangedAnnotationApplied += item => changed = item;

        await _cache.RefreshChangedAsync(path);

        Assert.Multiple(() =>
        {
            Assert.That(changed, Is.SameAs(preview));
            Assert.That(preview.Rating, Is.EqualTo(2));
            Assert.That(preview.Tags, Is.EqualTo(new[] { "fresh" }));
            Assert.That(preview.AnnotationSnapshot, Is.SameAs(_annotations.Snapshots[path]));
        });
    }

    [Test]
    public async Task LoadCachedDirectoryAsync_WhenCancelled_PropagatesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await _cache.LoadCachedDirectoryAsync(_directory, cancellation.Token));
    }

    private string CreateMediaFile(string name)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllBytes(path, [1]);
        return path;
    }

    private static IndexedMediaAnnotations CreateIndexed(string path, int rating, string[] tags) =>
        new(path, new MediaAnnotations(rating, tags), 1, DateTime.UtcNow, 0, DateTime.MinValue, DateTime.UtcNow);

    private static MediaAnnotationsSnapshot CreateSnapshot(string path, int rating, string[] tags) =>
        new(
            path,
            new MediaAnnotations(rating, tags),
            new AnnotationFileState(path, true, 1, DateTime.UtcNow, 1, DateTime.UtcNow));

    private static MediaPreviewViewModel CreatePreview(string path, MediaFormat format) =>
        new(
            Path.GetFileName(path),
            path,
            format,
            200,
            new FakeMediaPreviewService(),
            new FakePreviewOperationsService(),
            new FakeNotificationService(),
            new FakeFileAssociationsService(),
            new Mapper(),
            new LoggerConfiguration().CreateLogger(),
            LocalizationManager.Instance);

    private sealed class ImmediateSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback callback, object? state) => callback(state);
    }

    private sealed class FakeAnnotationsService : IMediaAnnotationsService
    {
        public IReadOnlyList<IndexedMediaAnnotations> DirectoryEntries { get; set; } = [];
        public Dictionary<string, MediaAnnotationsSnapshot> Snapshots { get; } = [];
        public IObservable<AnnotationsReconciliationStatus> ReconciliationStatus => Observable.Empty<AnnotationsReconciliationStatus>();
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<MediaAnnotations> GetAsync(string mediaPath, CancellationToken cancellationToken = default) =>
            Task.FromResult(Snapshots[mediaPath].Annotations);
        public Task<MediaAnnotationsSnapshot> GetSnapshotAsync(string mediaPath, CancellationToken cancellationToken = default) =>
            Task.FromResult(Snapshots[mediaPath]);
        public Task<MediaAnnotationsSnapshot> SaveAsync(string mediaPath, MediaAnnotations annotations, AnnotationFileState? expectedState = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<IndexedMediaAnnotations>> GetDirectoryAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(DirectoryEntries);
        }
        public Task<IReadOnlyList<IndexedMediaAnnotations>> SearchAsync(MediaAnnotationsSearchQuery query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<string>> GetAllTagsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);
        public Task RegisterDirectoryAsync(string directoryPath, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RefreshDirectoryAsync(string directoryPath, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RegisterRootsAsync(IEnumerable<string> roots, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RebuildRootsAsync(IEnumerable<string> roots, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ReconcileRegisteredRootsAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StartBackgroundReconciliationAsync(TimeSpan interval, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopBackgroundReconciliationAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeMediaPreviewService : IMediaPreviewService
    {
        public Task<Stream> GetJpegImageStreamAsync(MediaPreview imagePreview, MediaPreviewSize imagePreviewSize, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MediaPreview?> GetMediaPreviewAsync(string imagePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MediaPreview?> GetMediaPreviewAsync(FileModel fileModel, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IMediaMetadata> GetMediaMetadataAsync(MediaPreview mediaPreview, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DateTime> GetCreationDateTime(MediaPreview mediaPreview) => throw new NotSupportedException();
    }

    private sealed class FakeFileAssociationsService : IFileAssociationsService
    {
        public IEnumerable<FileApplicationInfo> GetAssociations(MediaFormat mediaFormat) => [];
    }

    private sealed class FakeNotificationService : INotificationService
    {
        public IObservable<Notification> NotificationReceived => Observable.Empty<Notification>();
        public void SendNotification(Notification notification) { }
    }

    private sealed class FakePreviewOperationsService : IMediaPreviewOperationsService
    {
        public IObservable<SelectedMediaPreview> ImagePreviewSelected => Observable.Empty<SelectedMediaPreview>();
        public IObservable<SelectedMediaPreview> MediaPreviewNavigationRequested => Observable.Empty<SelectedMediaPreview>();
        public IObservable<MediaFileOperationCompleted> FileOperationCompleted => Observable.Empty<MediaFileOperationCompleted>();
        public IObservable<IReadOnlyList<DeletedMediaFile>> DeletedFilesChanged => Observable.Empty<IReadOnlyList<DeletedMediaFile>>();
        public bool CanUndoDelete => false;
        public Task<OperationResult> CopyImagePreviewToDirectoryAsync(MediaPreview imagePreview, string selectedFolderPath, Progress<OperationInfo> progress) => throw new NotSupportedException();
        public Task<OperationResult> CopyImagePreviewToDirectoryAsync(MediaPreview imagePreview, string selectedFolderPath, Progress<OperationInfo> progress, string destinationFileName) => throw new NotSupportedException();
        public Task<OperationResult> MoveImagePreviewToDirectoryAsync(MediaPreview imagePreview, string selectedFolderPath, Progress<OperationInfo> progress) => throw new NotSupportedException();
        public Task<OperationResult> MoveImagePreviewToDirectoryAsync(MediaPreview imagePreview, string selectedFolderPath, Progress<OperationInfo> progress, string destinationFileName) => throw new NotSupportedException();
        public Task<OperationResult> DeleteImagePreviewAsync(MediaPreview imagePreview) => throw new NotSupportedException();
        public IReadOnlyList<DeletedMediaFile> GetDeletedFiles() => [];
        public Task<OperationResult> UndoLastDeleteAsync() => throw new NotSupportedException();
        public Task<OperationResult> RestoreDeletedFileAsync(string bufferPath) => throw new NotSupportedException();
        public Task<OperationResult> PermanentlyDeleteAsync(string bufferPath) => throw new NotSupportedException();
        public void SetSelectedPreview(SelectedMediaPreview selectedImagePreview) { }
        public void RequestMediaPreviewNavigation(SelectedMediaPreview selectedImagePreview) { }
        public void OpenInExternalProcess(MediaPreview mediaPreview, string pathToExecutable) { }
        public void OpenInExternalProcess(MediaPreview mediaPreview, FileApplicationInfo application) { }
        public MediaPreview? GetLastSelectedMediaPreview() => null;
    }
}

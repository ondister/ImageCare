using System.Reactive.Linq;

using Avalonia.Skia;

using ImageCare.Core.Domain;
using ImageCare.Core.Domain.Folders;
using ImageCare.Core.Domain.Media;
using ImageCare.Core.Domain.Media.Metadata;
using ImageCare.Core.Domain.MediaFormats;
using ImageCare.Core.Domain.Notification;
using ImageCare.Core.Domain.Preview;
using ImageCare.Core.Services.FileAssociationsService;
using ImageCare.Core.Services.MediaPreviewOperationsService;
using ImageCare.Core.Services.MediaPreviewService;
using ImageCare.Core.Services.NotificationService;
using ImageCare.UI.Avalonia.Localization;
using ImageCare.UI.Avalonia.Services;
using ImageCare.UI.Avalonia.ViewModels.Domain;

using Mapster;

using MapsterMapper;

using Serilog;

namespace ImageCare.UI.Avalonia.Tests.Services;

[TestFixture]
[NonParallelizable]
public sealed class MediaPreviewLoaderTests
{
    private static string ImageFixturePath => Path.Combine(
        TestContext.CurrentContext.TestDirectory,
        "Domain",
        "Media",
        "Assets",
        "unknown_media_preview.jpg");

    private FakeMediaPreviewService _previewService = null!;
    private IMapper _mapper = null!;
    private MediaPreviewLoader _loader = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp() => SkiaPlatform.Initialize();

    [SetUp]
    public void SetUp()
    {
        _previewService = new FakeMediaPreviewService();
        var mapperConfig = new TypeAdapterConfig();
        mapperConfig.NewConfig<MediaPreview, MediaPreviewViewModel>()
            .MapWith(source => CreatePreview(source));
        mapperConfig.NewConfig<MediaPreviewViewModel, MediaPreview>()
            .MapWith(source => new MediaPreview(
                source.Title,
                source.Url,
                source.MediaFormat,
                source.MaxImageHeight));
        _mapper = new Mapper(mapperConfig);
        _loader = new MediaPreviewLoader(_previewService, _mapper);
    }

    [Test]
    public async Task CreateAsync_CreatesMappedCardAndCopiesFileDate()
    {
        var created = new DateTime(2025, 3, 14, 9, 26, 0, DateTimeKind.Local);
        var file = new FileModel("photo.jpg", "/photos/photo.jpg", created);
        _previewService.FilePreview = new MediaPreview(
            file.Name,
            file.FullName,
            MediaFormat.MediaFormatJpg,
            200);

        var result = await _loader.CreateAsync<MediaPreviewViewModel>(file, CancellationToken.None);

        Assert.That(result, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(_previewService.RequestedFile, Is.SameAs(file));
            Assert.That(result!.Url, Is.EqualTo(file.FullName));
            Assert.That(result.FileDate, Is.EqualTo(created));
            Assert.That(result.DateTimeString, Is.EqualTo("14.03.2025 09:26"));
        });
    }

    [Test]
    public async Task LoadAsync_LoadsMetadataAndHonorsRequestedPreviewQuality()
    {
        var path = "/photos/photo.jpg";
        var source = new MediaPreview("photo.jpg", path, MediaFormat.MediaFormatJpg, 200);
        var preview = CreatePreview(source);
        var created = new DateTime(2024, 7, 2, 18, 45, 0, DateTimeKind.Local);
        _previewService.PathPreview = source;
        _previewService.Metadata = new UnsupportedMediaMetadata(created)
        {
            Orientation = ExifOrientation.RightTop
        };

        await _loader.LoadAsync(
            preview,
            512,
            MediaPreviewSize.Medium,
            refreshMetadata: false,
            CancellationToken.None);
        await _loader.LoadAsync(
            preview,
            1_024,
            MediaPreviewSize.Large,
            refreshMetadata: false,
            CancellationToken.None);
        await _loader.LoadAsync(
            preview,
            768,
            MediaPreviewSize.Large,
            refreshMetadata: false,
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(_previewService.RequestedPath, Is.EqualTo(path));
            Assert.That(_previewService.MetadataCallCount, Is.EqualTo(1));
            Assert.That(_previewService.RequestedPreviewSizes,
                Is.EqualTo(new[] { MediaPreviewSize.Medium, MediaPreviewSize.Large }));
            Assert.That(preview.Metadata, Is.SameAs(_previewService.Metadata));
            Assert.That(preview.MetadataString, Is.EqualTo("-"));
            Assert.That(preview.DateTimeString, Is.EqualTo("02.07.2024 18:45"));
            Assert.That(preview.RotateAngle, Is.EqualTo(90));
            Assert.That(preview.LoadedPreviewWidth, Is.EqualTo(1_024));
            Assert.That(preview.PreviewBitmap, Is.Not.Null);
        });

        _loader.Unload(preview);
    }

    [Test]
    public async Task UnloadOutside_UnloadsOnlyTrackedCardsOutsideRetainedSet()
    {
        var first = CreatePreview(new MediaPreview("first", "/photos/first.jpg", MediaFormat.MediaFormatJpg, 200));
        var retained = CreatePreview(new MediaPreview("retained", "/photos/retained.jpg", MediaFormat.MediaFormatJpg, 200));
        var last = CreatePreview(new MediaPreview("last", "/photos/last.jpg", MediaFormat.MediaFormatJpg, 200));

        await _loader.LoadBitmapAsync(first, 512, MediaPreviewSize.Medium, CancellationToken.None);
        await _loader.LoadBitmapAsync(retained, 512, MediaPreviewSize.Medium, CancellationToken.None);
        await _loader.LoadBitmapAsync(last, 512, MediaPreviewSize.Medium, CancellationToken.None);

        _loader.UnloadOutside(new HashSet<MediaPreviewViewModel> { retained });

        Assert.Multiple(() =>
        {
            Assert.That(first.PreviewBitmap, Is.Null);
            Assert.That(first.LoadedPreviewWidth, Is.Zero);
            Assert.That(retained.PreviewBitmap, Is.Not.Null);
            Assert.That(retained.LoadedPreviewWidth, Is.EqualTo(512));
            Assert.That(last.PreviewBitmap, Is.Null);
            Assert.That(last.LoadedPreviewWidth, Is.Zero);
        });

        _loader.UnloadAll([retained]);
    }

    private MediaPreviewViewModel CreatePreview(MediaPreview source) =>
        new(
            source.Title,
            source.Url,
            source.MediaFormat,
            source.MaxImageHeight,
            _previewService,
            new FakePreviewOperationsService(),
            new FakeNotificationService(),
            new FakeFileAssociationsService(),
            _mapper,
            new LoggerConfiguration().CreateLogger(),
            LocalizationManager.Instance);

    private sealed class FakeMediaPreviewService : IMediaPreviewService
    {
        public MediaPreview? FilePreview { get; set; }
        public MediaPreview? PathPreview { get; set; }
        public IMediaMetadata Metadata { get; set; } = new UnsupportedMediaMetadata(DateTime.MinValue);
        public FileModel? RequestedFile { get; private set; }
        public string? RequestedPath { get; private set; }
        public int MetadataCallCount { get; private set; }
        public List<MediaPreviewSize> RequestedPreviewSizes { get; } = [];

        public Task<Stream> GetJpegImageStreamAsync(
            MediaPreview imagePreview,
            MediaPreviewSize imagePreviewSize,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestedPreviewSizes.Add(imagePreviewSize);
            return Task.FromResult<Stream>(File.OpenRead(ImageFixturePath));
        }

        public Task<MediaPreview?> GetMediaPreviewAsync(
            string imagePath,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestedPath = imagePath;
            return Task.FromResult(PathPreview);
        }

        public Task<MediaPreview?> GetMediaPreviewAsync(
            FileModel fileModel,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestedFile = fileModel;
            return Task.FromResult(FilePreview);
        }

        public Task<IMediaMetadata> GetMediaMetadataAsync(
            MediaPreview mediaPreview,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            MetadataCallCount++;
            return Task.FromResult(Metadata);
        }

        public Task<DateTime> GetCreationDateTime(MediaPreview mediaPreview) =>
            Task.FromResult(Metadata.CreationDateTime);
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

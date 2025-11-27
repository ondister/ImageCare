using ImageCare.Core.Domain.Folders;
using ImageCare.Core.Domain.MediaFormats;
using ImageCare.Core.Domain.Preview;
using ImageCare.Core.Services.FileSystemService;
using ImageCare.Core.Services.FolderStatisticsService;
using ImageCare.Core.Services.MediaPreviewService;
using Moq;
using NUnit.Framework.Internal;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;

namespace ImageCare.Core.Tests.Services.FolderStatisticsService;

[TestFixture]
public class FileWatcherFolderStatisticsServiceTests
{
    private string _testDirectory;
    private Mock<IMediaPreviewService> _previewServiceMock;
    private Mock<IFileSystemService> _fileSystemServiceMock;
    private FileWatcherFolderStatisticsService _service;

    [SetUp]
    public void SetUp()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testDirectory);

        _previewServiceMock = new Mock<IMediaPreviewService>();
        _fileSystemServiceMock = new Mock<IFileSystemService>();
        _service = new FileWatcherFolderStatisticsService(_previewServiceMock.Object, _fileSystemServiceMock.Object);
    }

    [TearDown]
    public void TearDown()
    {
        _service?.Dispose();

        try
        {
            if (Directory.Exists(_testDirectory))
            {
                Directory.Delete(_testDirectory, true);
            }
        }
        catch { }
    }

    [Test]
    public void StartAsync_WhenDirectoryNotExists_ThrowsDirectoryNotFoundException()
    {
        var nonExistentPath = Path.Combine(_testDirectory, "nonexistent");

        Assert.ThrowsAsync<DirectoryNotFoundException>(() =>
                                                           _service.StartAsync(nonExistentPath));
    }

    [Test]
    public async Task StartAsync_WhenDirectoryExists_StartsScanning()
    {
        SetupFileSystemServiceReturnsNoFiles();
        _previewServiceMock
            .Setup(x => x.GetMediaPreviewAsync(It.IsAny<string>()))
            .ReturnsAsync((MediaPreview?)null);

        await _service.StartAsync(_testDirectory);

        Assert.That(_service.IsScanning, Is.True);
        _service.Stop();
    }

    [Test]
    public async Task StartAsync_WithSupportedFiles_ProcessesFilesAndUpdatesBuckets()
    {
        var testFile = Path.Combine(_testDirectory, "test.jpg");
        var creationDate = DateTime.Now.Date;

        var mediaPreview = new MediaPreview("Test", testFile, MediaFormat.MediaFormatJpg, 1000);
        var fileModel = new FileModel("test.jpg", testFile, creationDate);

        SetupFileSystemServiceReturnsFiles(new[] { fileModel });
        _previewServiceMock
            .Setup(x => x.GetMediaPreviewAsync(testFile))
            .ReturnsAsync(mediaPreview);
        _previewServiceMock
            .Setup(x => x.GetCreationDateTime(mediaPreview))
            .ReturnsAsync(creationDate);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var bucketEvent = _service.BucketChanged.FirstAsync().ToTask(cts.Token);

        await _service.StartAsync(_testDirectory, cancellationToken: cts.Token);
        var bucket = await bucketEvent;

        Assert.That(bucket, Is.Not.Null);
        Assert.That(_service.CurrentTotalFiles, Is.EqualTo(1));
        _service.Stop();
    }

    [Test]
    public async Task StartAsync_WithUnsupportedFiles_IgnoresUnsupportedFormats()
    {
        var supportedFile = Path.Combine(_testDirectory, "test.jpg");
        var unsupportedFile = Path.Combine(_testDirectory, "test.txt");

        var supportedFileModel = new FileModel("test.jpg", supportedFile, DateTime.Now);
        var unsupportedFileModel = new FileModel("test.txt", unsupportedFile, DateTime.Now);

        SetupFileSystemServiceReturnsFiles(new[] { supportedFileModel, unsupportedFileModel });

        var mediaPreview = new MediaPreview("Test", supportedFile, MediaFormat.MediaFormatJpg, 1000);
        _previewServiceMock
            .Setup(x => x.GetMediaPreviewAsync(supportedFile))
            .ReturnsAsync(mediaPreview);
        _previewServiceMock
            .Setup(x => x.GetCreationDateTime(mediaPreview))
            .ReturnsAsync(DateTime.Now);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var totalFilesEvent = _service.TotalFilesCount.FirstAsync(x => x > 0).ToTask(cts.Token);

        await _service.StartAsync(_testDirectory, cancellationToken: cts.Token);
        await totalFilesEvent;

        _previewServiceMock.Verify(x => x.GetMediaPreviewAsync(supportedFile), Times.Once);
        _previewServiceMock.Verify(x => x.GetMediaPreviewAsync(unsupportedFile), Times.Never);
        Assert.That(_service.CurrentTotalFiles, Is.EqualTo(1));
        _service.Stop();
    }

    [Test]
    public async Task StartAsync_WhenPreviewServiceReturnsNull_SkipsFile()
    {
        var testFile = Path.Combine(_testDirectory, "test.jpg");
        var fileModel = new FileModel("test.jpg", testFile, DateTime.Now);

        SetupFileSystemServiceReturnsFiles(new[] { fileModel });
        _previewServiceMock
            .Setup(x => x.GetMediaPreviewAsync(testFile))
            .ReturnsAsync((MediaPreview?)null);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var scanCompleted = _service.ScanProgress
                                    .FirstAsync(x => x.Status == ScanStatus.InitialScanCompleted)
                                    .ToTask(cts.Token);

        await _service.StartAsync(_testDirectory, cancellationToken: cts.Token);
        await scanCompleted;

        Assert.That(_service.CurrentTotalFiles, Is.EqualTo(0));
        _service.Stop();
    }

    [Test]
    public async Task StartAsync_WhenPreviewServiceReturnsEmptyMediaPreview_SkipsFile()
    {
        var testFile = Path.Combine(_testDirectory, "test.jpg");
        var fileModel = new FileModel("test.jpg", testFile, DateTime.Now);

        SetupFileSystemServiceReturnsFiles(new[] { fileModel });
        _previewServiceMock
            .Setup(x => x.GetMediaPreviewAsync(testFile))
            .ReturnsAsync(MediaPreview.Empty);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var scanCompleted = _service.ScanProgress
                                    .FirstAsync(x => x.Status == ScanStatus.InitialScanCompleted)
                                    .ToTask(cts.Token);

        await _service.StartAsync(_testDirectory, cancellationToken: cts.Token);
        await scanCompleted;

        Assert.That(_service.CurrentTotalFiles, Is.EqualTo(0));
        _service.Stop();
    }

    [Test]
    public async Task Stop_WhileScanning_StopsService()
    {
        SetupFileSystemServiceReturnsNoFiles();
        _previewServiceMock
            .Setup(x => x.GetMediaPreviewAsync(It.IsAny<string>()))
            .ReturnsAsync((MediaPreview?)null);

        await _service.StartAsync(_testDirectory);

        _service.Stop();

        Assert.That(_service.IsScanning, Is.False);
    }

    [Test]
    public void Dispose_CanBeCalledMultipleTimes_WithoutException()
    {
        _service.Dispose();
        Assert.DoesNotThrow(() => _service.Dispose());
    }

    [Test]
    public async Task ScanProgress_ReportsInitialScanStatuses()
    {
        SetupFileSystemServiceReturnsNoFiles();
        _previewServiceMock
            .Setup(x => x.GetMediaPreviewAsync(It.IsAny<string>()))
            .ReturnsAsync((MediaPreview?)null);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var initialScanStarted = _service.ScanProgress
                                         .FirstAsync(x => x.Status == ScanStatus.InitialScanStarted)
                                         .ToTask(cts.Token);

        await _service.StartAsync(_testDirectory, cancellationToken: cts.Token);
        var progress = await initialScanStarted;

        Assert.That(progress.Status, Is.EqualTo(ScanStatus.InitialScanStarted));
        _service.Stop();
    }

    [Test]
    public async Task TotalFilesCount_UpdatesWhenFilesAreProcessed()
    {
        var testFile = Path.Combine(_testDirectory, "test.jpg");
        File.WriteAllText(testFile, "test text");
        var creationDate = DateTime.Now;

        var mediaPreview = new MediaPreview("Test", testFile, MediaFormat.MediaFormatJpg, 1000);
        var fileModel = new FileModel("test.jpg", testFile, creationDate);

        SetupFileSystemServiceReturnsFiles(new[] { fileModel });
        _previewServiceMock
            .Setup(x => x.GetMediaPreviewAsync(testFile))
            .ReturnsAsync(mediaPreview);
        _previewServiceMock
            .Setup(x => x.GetCreationDateTime(mediaPreview))
            .ReturnsAsync(creationDate);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var totalFilesEvent = _service.TotalFilesCount
                                      .Where(x => x == 1)
                                      .FirstAsync()
                                      .ToTask(cts.Token);

        await _service.StartAsync(_testDirectory, cancellationToken: cts.Token);
        var totalFiles = await totalFilesEvent;

        Assert.That(totalFiles, Is.EqualTo(1));
        _service.Stop();
    }

    [Test]
    public async Task StartAsync_WhenPreviewServiceThrows_ReportsErrorInProgress()
    {
        var testFile = Path.Combine(_testDirectory, "test.jpg");
        var fileModel = new FileModel("test.jpg", testFile, DateTime.Now);
        File.WriteAllText(testFile,"test text");

        SetupFileSystemServiceReturnsFiles(new[] { fileModel });
        _previewServiceMock
            .Setup(x => x.GetMediaPreviewAsync(testFile))
            .ThrowsAsync(new Exception("Preview service error"));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var errorProgress = _service.ScanProgress
                                    .Where(x => x.Status == ScanStatus.ErrorOccurred)
                                    .FirstAsync()
                                    .ToTask(cts.Token);

        await _service.StartAsync(_testDirectory, cancellationToken: cts.Token);
        var progress = await errorProgress;

        Assert.That(progress.Error, Is.Not.Null);
        _service.Stop();
    }

    [Test]
    public async Task StartAsync_WithMultipleSupportedFormats_ProcessesAllFormats()
    {
        var jpgFile = Path.Combine(_testDirectory, "test1.jpg");
        var arwFile = Path.Combine(_testDirectory, "test2.arw");
        var cr3File = Path.Combine(_testDirectory, "test3.cr3");
        File.WriteAllText(jpgFile, "test text");
        File.WriteAllText(arwFile, "test text");
        File.WriteAllText(cr3File, "test text");

        var jpgFileModel = new FileModel("test1.jpg", jpgFile, DateTime.Now);
        var arwFileModel = new FileModel("test2.arw", arwFile, DateTime.Now);
        var cr3FileModel = new FileModel("test3.cr3", cr3File, DateTime.Now);

        SetupFileSystemServiceReturnsFiles(new[] { jpgFileModel, arwFileModel, cr3FileModel });

        var jpgPreview = new MediaPreview("JPG", jpgFile, MediaFormat.MediaFormatJpg, 1000);
        var arwPreview = new MediaPreview("ARW", arwFile, MediaFormat.MediaFormatArw, 1000);
        var cr3Preview = new MediaPreview("CR3", cr3File, MediaFormat.MediaFormatCr3, 1000);

        _previewServiceMock
            .Setup(x => x.GetMediaPreviewAsync(jpgFile))
            .ReturnsAsync(jpgPreview);
        _previewServiceMock
            .Setup(x => x.GetMediaPreviewAsync(arwFile))
            .ReturnsAsync(arwPreview);
        _previewServiceMock
            .Setup(x => x.GetMediaPreviewAsync(cr3File))
            .ReturnsAsync(cr3Preview);
        _previewServiceMock
            .Setup(x => x.GetCreationDateTime(It.IsAny<MediaPreview>()))
            .ReturnsAsync(DateTime.Now);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var totalFilesEvent = _service.TotalFilesCount
                                      .Where(x => x == 3)
                                      .FirstAsync()
                                      .ToTask(cts.Token);

        await _service.StartAsync(_testDirectory, cancellationToken: cts.Token);
        var totalFiles = await totalFilesEvent;

        Assert.That(totalFiles, Is.EqualTo(3));
        _service.Stop();
    }

    [Test]
    public async Task StartAsync_WhenFileHasNoCreationDate_SkipsFile()
    {
        var testFile = Path.Combine(_testDirectory, "test.jpg");
        var fileModel = new FileModel("test.jpg", testFile, DateTime.Now);

        SetupFileSystemServiceReturnsFiles(new[] { fileModel });

        var mediaPreview = new MediaPreview("Test", testFile, MediaFormat.MediaFormatJpg, 1000);
        _previewServiceMock
            .Setup(x => x.GetMediaPreviewAsync(testFile))
            .ReturnsAsync(mediaPreview);
        _previewServiceMock
            .Setup(x => x.GetCreationDateTime(mediaPreview))
            .ThrowsAsync(new InvalidOperationException("No creation date"));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var finalProgress = _service.ScanProgress
                                    .FirstAsync(x => x.Status == ScanStatus.InitialScanCompleted || x.Status == ScanStatus.ErrorOccurred || x.Status == ScanStatus.Idle)
                                    .ToTask(cts.Token);

        await _service.StartAsync(_testDirectory, cancellationToken: cts.Token);
        await finalProgress;

        Assert.That(_service.CurrentTotalFiles, Is.EqualTo(0));
        _service.Stop();
    }

    private void SetupFileSystemServiceReturnsNoFiles()
    {
        _fileSystemServiceMock
            .Setup(x => x.EnumerateFiles(_testDirectory, "*.*", SearchOption.TopDirectoryOnly))
            .Returns(Enumerable.Empty<FileModel>());
    }

    private void SetupFileSystemServiceReturnsFiles(IEnumerable<FileModel> files)
    {
        _fileSystemServiceMock
            .Setup(x => x.EnumerateFiles(_testDirectory, "*.*", SearchOption.TopDirectoryOnly))
            .Returns(files);

        foreach (var file in files)
        {
            _fileSystemServiceMock
                .Setup(x => x.FileExists(file.FullName))
                .Returns(true);
        }
    }
}
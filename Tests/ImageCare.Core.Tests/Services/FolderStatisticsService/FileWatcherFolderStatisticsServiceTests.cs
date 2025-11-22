using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;

using ImageCare.Core.Domain.MediaFormats;
using ImageCare.Core.Domain.Preview;
using ImageCare.Core.Services.FolderStatisticsService;
using ImageCare.Core.Services.MediaPreviewService;

using Moq;

namespace ImageCare.Core.Tests.Services.FolderStatisticsService;

[TestFixture]
public class FileWatcherFolderStatisticsServiceTests
{
    private string _testDirectory;
    private Mock<IMediaPreviewService> _previewServiceMock;
    private FileWatcherFolderStatisticsService _service;

    [SetUp]
    public void SetUp()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testDirectory);

        _previewServiceMock = new Mock<IMediaPreviewService>();
        _service = new FileWatcherFolderStatisticsService(_previewServiceMock.Object);
    }

    [TearDown]
    public void TearDown()
    {
        _service?.Dispose();
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, true);
        }
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
        var mediaPreview = new MediaPreview("Test", "test.jpg", MediaFormat.MediaFormatJpg, 1000);
        _previewServiceMock
            .Setup(x => x.GetMediaPreviewAsync(It.IsAny<string>()))
            .ReturnsAsync(mediaPreview);
        _previewServiceMock
            .Setup(x => x.GetCreationDateTime(It.IsAny<MediaPreview>()))
            .ReturnsAsync(DateTime.Now);

        await _service.StartAsync(_testDirectory);

        Assert.That(_service.IsScanning, Is.True);
    }

    [Test]
    public async Task StartAsync_WithSupportedFiles_ProcessesFilesAndUpdatesBuckets()
    {
        var testFile = Path.Combine(_testDirectory, "test.jpg");
        await File.WriteAllTextAsync(testFile, "test content");

        var mediaPreview = new MediaPreview("Test", testFile, MediaFormat.MediaFormatJpg, 1000);
        _previewServiceMock
            .Setup(x => x.GetMediaPreviewAsync(testFile))
            .ReturnsAsync(mediaPreview);
        _previewServiceMock
            .Setup(x => x.GetCreationDateTime(mediaPreview))
            .ReturnsAsync(DateTime.Now.Date);

        var bucketEvent = _service.BucketChanged.FirstAsync().ToTask();

        await _service.StartAsync(_testDirectory);

        var bucket = await bucketEvent;

        Assert.That(bucket, Is.Not.Null);
        Assert.That(_service.CurrentTotalFiles, Is.EqualTo(1));
    }

    [Test]
    public async Task StartAsync_WithUnsupportedFiles_IgnoresUnsupportedFormats()
    {
        var supportedFile = Path.Combine(_testDirectory, "test.jpg");
        var unsupportedFile = Path.Combine(_testDirectory, "test.txt");

        await File.WriteAllTextAsync(supportedFile, "test content");
        await File.WriteAllTextAsync(unsupportedFile, "test content");

        var mediaPreview = new MediaPreview("Test", supportedFile, MediaFormat.MediaFormatJpg, 1000);
        _previewServiceMock
            .Setup(x => x.GetMediaPreviewAsync(supportedFile))
            .ReturnsAsync(mediaPreview);
        _previewServiceMock
            .Setup(x => x.GetCreationDateTime(mediaPreview))
            .ReturnsAsync(DateTime.Now);

        var totalFilesEvent = _service.TotalFilesCount.FirstAsync(x => x > 0).ToTask();

        await _service.StartAsync(_testDirectory);

        await totalFilesEvent;

        _previewServiceMock.Verify(x => x.GetMediaPreviewAsync(supportedFile), Times.Once);
        _previewServiceMock.Verify(x => x.GetMediaPreviewAsync(unsupportedFile), Times.Never);
        Assert.That(_service.CurrentTotalFiles, Is.EqualTo(1));
    }

    [Test]
    public async Task StartAsync_WhenPreviewServiceReturnsNull_SkipsFile()
    {
        var testFile = Path.Combine(_testDirectory, "test.jpg");
        await File.WriteAllTextAsync(testFile, "test content");

        _previewServiceMock
            .Setup(x => x.GetMediaPreviewAsync(testFile))
            .ReturnsAsync((MediaPreview?)null);

        var scanCompleted = _service.ScanProgress
                                    .FirstAsync(x => x.Status == ScanStatus.InitialScanCompleted)
                                    .ToTask();

        await _service.StartAsync(_testDirectory);

        await scanCompleted;

        Assert.That(_service.CurrentTotalFiles, Is.EqualTo(0));
    }

    [Test]
    public async Task StartAsync_WhenPreviewServiceReturnsEmptyMediaPreview_SkipsFile()
    {
        var testFile = Path.Combine(_testDirectory, "test.jpg");
        await File.WriteAllTextAsync(testFile, "test content");

        _previewServiceMock
            .Setup(x => x.GetMediaPreviewAsync(testFile))
            .ReturnsAsync(MediaPreview.Empty);

        var scanCompleted = _service.ScanProgress
                                    .FirstAsync(x => x.Status == ScanStatus.InitialScanCompleted)
                                    .ToTask();

        await _service.StartAsync(_testDirectory);

        await scanCompleted;

        Assert.That(_service.CurrentTotalFiles, Is.EqualTo(0));
    }

    [Test]
    public async Task Stop_WhileScanning_StopsService()
    {
        var mediaPreview = new MediaPreview("Test", "test.jpg", MediaFormat.MediaFormatJpg, 1000);
        _previewServiceMock
            .Setup(x => x.GetMediaPreviewAsync(It.IsAny<string>()))
            .ReturnsAsync(mediaPreview);
        _previewServiceMock
            .Setup(x => x.GetCreationDateTime(It.IsAny<MediaPreview>()))
            .ReturnsAsync(DateTime.Now);

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
        var mediaPreview = new MediaPreview("Test", "test.jpg", MediaFormat.MediaFormatJpg, 1000);
        _previewServiceMock
            .Setup(x => x.GetMediaPreviewAsync(It.IsAny<string>()))
            .ReturnsAsync(mediaPreview);
        _previewServiceMock
            .Setup(x => x.GetCreationDateTime(It.IsAny<MediaPreview>()))
            .ReturnsAsync(DateTime.Now);

        var initialScanStarted = _service.ScanProgress
                                         .FirstAsync(x => x.Status == ScanStatus.InitialScanStarted)
                                         .ToTask();

        await _service.StartAsync(_testDirectory);

        var progress = await initialScanStarted;

        Assert.That(progress.Status, Is.EqualTo(ScanStatus.InitialScanStarted));
    }

    [Test]
    public async Task TotalFilesCount_UpdatesWhenFilesAreProcessed()
    {
        var testFile = Path.Combine(_testDirectory, "test.jpg");
        await File.WriteAllTextAsync(testFile, "test content");

        var mediaPreview = new MediaPreview("Test", testFile, MediaFormat.MediaFormatJpg, 1000);
        _previewServiceMock
            .Setup(x => x.GetMediaPreviewAsync(testFile))
            .ReturnsAsync(mediaPreview);
        _previewServiceMock
            .Setup(x => x.GetCreationDateTime(mediaPreview))
            .ReturnsAsync(DateTime.Now);

        var totalFilesEvent = _service.TotalFilesCount.FirstAsync(x => x == 1).ToTask();

        await _service.StartAsync(_testDirectory);

        var totalFiles = await totalFilesEvent;

        Assert.That(totalFiles, Is.EqualTo(1));
    }


    [Test]
    public async Task StartAsync_WhenPreviewServiceThrows_ReportsErrorInProgress()
    {
        var testFile = Path.Combine(_testDirectory, "test.jpg");
        await File.WriteAllTextAsync(testFile, "test content");

        _previewServiceMock
            .Setup(x => x.GetMediaPreviewAsync(testFile))
            .ThrowsAsync(new Exception("Preview service error"));

        var errorProgress = _service.ScanProgress
                                    .FirstAsync(x => x.Status == ScanStatus.ErrorOccurred)
                                    .ToTask();

        await _service.StartAsync(_testDirectory);

        var progress = await errorProgress;

        Assert.That(progress.Error, Is.Not.Null);
    }

    [Test]
    public async Task StartAsync_WithMultipleSupportedFormats_ProcessesAllFormats()
    {
        var jpgFile = Path.Combine(_testDirectory, "test1.jpg");
        var arwFile = Path.Combine(_testDirectory, "test2.arw");
        var cr3File = Path.Combine(_testDirectory, "test3.cr3");

        await File.WriteAllTextAsync(jpgFile, "test content");
        await File.WriteAllTextAsync(arwFile, "test content");
        await File.WriteAllTextAsync(cr3File, "test content");

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

        var totalFilesEvent = _service.TotalFilesCount.FirstAsync(x => x == 3).ToTask();

        await _service.StartAsync(_testDirectory);

        var totalFiles = await totalFilesEvent;

        Assert.That(totalFiles, Is.EqualTo(3));
    }

    [Test]
    public async Task StartAsync_WhenFileHasNoCreationDate_SkipsFile()
    {
        var testFile = Path.Combine(_testDirectory, "test.jpg");
        await File.WriteAllTextAsync(testFile, "test content");

        var mediaPreview = new MediaPreview("Test", testFile, MediaFormat.MediaFormatJpg, 1000);
        _previewServiceMock
            .Setup(x => x.GetMediaPreviewAsync(testFile))
            .ReturnsAsync(mediaPreview);
        _previewServiceMock
            .Setup(x => x.GetCreationDateTime(mediaPreview))
            .ThrowsAsync(new InvalidOperationException("No creation date"));

        // Wait for any final status
        var finalProgress = _service.ScanProgress
                                    .FirstAsync(x => x.Status == ScanStatus.InitialScanCompleted ||
                                                     x.Status == ScanStatus.ErrorOccurred ||
                                                     x.Status == ScanStatus.Idle)
                                    .ToTask();

        await _service.StartAsync(_testDirectory);

        await finalProgress;

        Assert.That(_service.CurrentTotalFiles, Is.EqualTo(0));
    }
}
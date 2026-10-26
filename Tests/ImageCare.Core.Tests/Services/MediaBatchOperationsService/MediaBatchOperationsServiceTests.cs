using System.Reflection;

using ImageCare.Core.Domain.Annotations;
using ImageCare.Core.Domain.MediaFormats;
using ImageCare.Core.Domain.Preview;
using ImageCare.Core.Exceptions;
using ImageCare.Core.Services.FileSystemService;
using ImageCare.Core.Services.MediaAnnotationsService;
using ImageCare.Core.Services.MediaBatchOperationsService;
using ImageCare.Core.Services.MediaPreviewOperationsService;
using ImageCare.Core.Services.MediaPreviewService;

using BatchService = ImageCare.Core.Services.MediaBatchOperationsService.MediaBatchOperationsService;

using Moq;

namespace ImageCare.Core.Tests.Services.MediaBatchOperationsService;

[TestFixture]
public class MediaBatchOperationsServiceTests
{
    private string _root = string.Empty;
    private string _destination = string.Empty;
    private string _testImagePath = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), $"ImageCareBatchTests_{Guid.NewGuid():N}");
        _destination = Path.Combine(_root, "export");
        Directory.CreateDirectory(_destination);

        var assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        _testImagePath = Path.Combine(assemblyDirectory, "TestImages", "test.JPG");
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Test]
    public async Task CopyAsync_WhenAllItemsSucceed_ReturnsSummaryAndProgress()
    {
        var operations = new Mock<IMediaPreviewOperationsService>();
        operations
            .Setup(service => service.CopyImagePreviewToDirectoryAsync(
                It.IsAny<MediaPreview>(),
                _destination,
                It.IsAny<Progress<OperationInfo>>()))
            .ReturnsAsync(OperationResult.Success);

        var service = CreateService(operations.Object);
        var progressReports = new List<MediaBatchOperationProgress>();
        var items = CreatePreviews("one.jpg", "two.jpg", "three.jpg");

        var summary = await service.CopyAsync(
            items,
            _destination,
            new DelegateProgress<MediaBatchOperationProgress>(progressReports.Add));

        Assert.Multiple(() =>
        {
            Assert.That(summary.Kind, Is.EqualTo(MediaBatchOperationKind.Copy));
            Assert.That(summary.Total, Is.EqualTo(3));
            Assert.That(summary.Succeeded, Is.EqualTo(3));
            Assert.That(summary.Failed, Is.Zero);
            Assert.That(summary.Cancelled, Is.False);
            Assert.That(progressReports, Has.Count.EqualTo(4));
            Assert.That(progressReports[^1].Processed, Is.EqualTo(3));
            Assert.That(progressReports[^1].Total, Is.EqualTo(3));
        });
    }

    [Test]
    public async Task CopyAsync_WhenAnItemFails_ContinuesAndCollectsFailure()
    {
        var operations = new Mock<IMediaPreviewOperationsService>();
        operations
            .SetupSequence(service => service.CopyImagePreviewToDirectoryAsync(
                It.IsAny<MediaPreview>(),
                _destination,
                It.IsAny<Progress<OperationInfo>>()))
            .ReturnsAsync(OperationResult.Success)
            .ThrowsAsync(new ServiceException("copy failed"))
            .ReturnsAsync(OperationResult.Success);

        var service = CreateService(operations.Object);
        var items = CreatePreviews("one.jpg", "two.jpg", "three.jpg");

        var summary = await service.CopyAsync(items, _destination);

        Assert.Multiple(() =>
        {
            Assert.That(summary.Total, Is.EqualTo(3));
            Assert.That(summary.Succeeded, Is.EqualTo(2));
            Assert.That(summary.Failed, Is.EqualTo(1));
            Assert.That(summary.Failures[0].SourcePath, Is.EqualTo(items[1].Url));
            Assert.That(summary.Failures[0].Error, Is.EqualTo("copy failed"));
        });
    }

    [Test]
    public async Task CopyAsync_WhenCancelledBetweenItems_StopsAndReportsCancellation()
    {
        var operations = new Mock<IMediaPreviewOperationsService>();
        operations
            .Setup(service => service.CopyImagePreviewToDirectoryAsync(
                It.IsAny<MediaPreview>(),
                _destination,
                It.IsAny<Progress<OperationInfo>>()))
            .ReturnsAsync(OperationResult.Success);

        var service = CreateService(operations.Object);
        var items = CreatePreviews("one.jpg", "two.jpg", "three.jpg");
        using var cancellation = new CancellationTokenSource();
        var progress = new DelegateProgress<MediaBatchOperationProgress>(
            report =>
            {
                if (report.Processed == 1)
                {
                    cancellation.Cancel();
                }
            });

        var summary = await service.CopyAsync(items, _destination, progress, cancellation.Token);

        Assert.Multiple(() =>
        {
            Assert.That(summary.Total, Is.EqualTo(3));
            Assert.That(summary.Succeeded, Is.EqualTo(1));
            Assert.That(summary.Cancelled, Is.True);
            operations.Verify(
                service => service.CopyImagePreviewToDirectoryAsync(
                    It.IsAny<MediaPreview>(),
                    _destination,
                    It.IsAny<Progress<OperationInfo>>()),
                Times.Once);
        });
    }

    [Test]
    public async Task DeleteAsync_WhenSingleOperationReportsFailure_CollectsFailure()
    {
        var operations = new Mock<IMediaPreviewOperationsService>();
        operations
            .Setup(service => service.DeleteImagePreviewAsync(It.IsAny<MediaPreview>()))
            .ReturnsAsync(OperationResult.Failed);

        var service = CreateService(operations.Object);
        var items = CreatePreviews("one.jpg");

        var summary = await service.DeleteAsync(items);

        Assert.Multiple(() =>
        {
            Assert.That(summary.Kind, Is.EqualTo(MediaBatchOperationKind.Delete));
            Assert.That(summary.Succeeded, Is.Zero);
            Assert.That(summary.Failed, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task ExportJpegAsync_WritesLargestPreviewAndCarriesAnnotations()
    {
        Assert.That(File.Exists(_testImagePath), Is.True, $"Test image is missing: {_testImagePath}");

        var previewService = new Mock<IMediaPreviewService>();
        previewService
            .Setup(service => service.GetJpegImageStreamAsync(
                It.IsAny<MediaPreview>(),
                MediaPreviewSize.Large,
                It.IsAny<CancellationToken>()))
            .Returns(() => Task.FromResult<Stream>(File.OpenRead(_testImagePath)));

        var annotations = new MediaAnnotations(5, ["bird", "lake"]);
        var annotationsService = new Mock<IMediaAnnotationsService>();
        annotationsService
            .Setup(service => service.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(annotations);
        annotationsService
            .Setup(service => service.SaveAsync(
                It.IsAny<string>(),
                It.IsAny<MediaAnnotations>(),
                It.IsAny<AnnotationFileState?>(),
                It.IsAny<CancellationToken>()))
            .Returns((string path, MediaAnnotations value, AnnotationFileState? _, CancellationToken _) =>
                Task.FromResult(new MediaAnnotationsSnapshot(
                    path,
                    value,
                    new AnnotationFileState(path, true, 0, DateTime.UtcNow, 0, DateTime.UtcNow))));

        var service = CreateService(
            new Mock<IMediaPreviewOperationsService>().Object,
            previewService.Object,
            annotationsService.Object);
        var firstSource = Path.Combine(_root, "first", "test.JPG");
        var secondSource = Path.Combine(_root, "second", "test.JPG");
        var items = new[]
        {
            new MediaPreview("First", firstSource, MediaFormat.MediaFormatJpg, 200),
            new MediaPreview("Second", secondSource, MediaFormat.MediaFormatJpg, 200)
        };

        var summary = await service.ExportJpegAsync(items, _destination);

        var exported = Directory.GetFiles(_destination, "*.jpg").OrderBy(path => path).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(summary.Kind, Is.EqualTo(MediaBatchOperationKind.ExportJpeg));
            Assert.That(summary.Succeeded, Is.EqualTo(2));
            Assert.That(summary.DestinationPaths, Has.Count.EqualTo(2));
            Assert.That(exported, Has.Length.EqualTo(2));
            Assert.That(exported.Select(Path.GetFileName), Is.EquivalentTo(new[] { "test (1).jpg", "test.jpg" }));
            Assert.That(exported.All(path => new FileInfo(path).Length > 0), Is.True);
        });
        annotationsService.Verify(
            service => service.SaveAsync(
                It.IsAny<string>(),
                annotations,
                It.IsAny<AnnotationFileState?>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));
        previewService.Verify(
            service => service.GetJpegImageStreamAsync(
                It.IsAny<MediaPreview>(),
                MediaPreviewSize.Large,
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Test]
    public async Task ExportJpegAsync_WhenAnnotationSaveFails_RemovesPartialFile()
    {
        Assert.That(File.Exists(_testImagePath), Is.True, $"Test image is missing: {_testImagePath}");

        var previewService = new Mock<IMediaPreviewService>();
        previewService
            .Setup(service => service.GetJpegImageStreamAsync(
                It.IsAny<MediaPreview>(),
                MediaPreviewSize.Large,
                It.IsAny<CancellationToken>()))
            .Returns(() => Task.FromResult<Stream>(File.OpenRead(_testImagePath)));

        var annotationsService = new Mock<IMediaAnnotationsService>();
        annotationsService
            .Setup(service => service.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MediaAnnotations(3, ["keep"]));
        annotationsService
            .Setup(service => service.SaveAsync(
                It.IsAny<string>(),
                It.IsAny<MediaAnnotations>(),
                It.IsAny<AnnotationFileState?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("embedding failed"));

        var service = CreateService(
            new Mock<IMediaPreviewOperationsService>().Object,
            previewService.Object,
            annotationsService.Object);
        var item = new MediaPreview("One", Path.Combine(_root, "one", "photo.jpg"), MediaFormat.MediaFormatJpg, 200);

        var summary = await service.ExportJpegAsync([item], _destination);

        Assert.Multiple(() =>
        {
            Assert.That(summary.Succeeded, Is.Zero);
            Assert.That(summary.Failed, Is.EqualTo(1));
            Assert.That(summary.Failures[0].Error, Is.EqualTo("embedding failed"));
            Assert.That(Directory.GetFiles(_destination, "*.jpg"), Is.Empty);
        });
    }

    private BatchService CreateService(
        IMediaPreviewOperationsService operationsService,
        IMediaPreviewService? previewService = null,
        IMediaAnnotationsService? annotationsService = null)
    {
        return new BatchService(
            operationsService,
            previewService ?? new Mock<IMediaPreviewService>().Object,
            annotationsService ?? new Mock<IMediaAnnotationsService>().Object,
            new WindowsFileSystemService());
    }

    private MediaPreview[] CreatePreviews(params string[] names)
    {
        return names
            .Select(name => new MediaPreview(name, Path.Combine(_root, name), MediaFormat.MediaFormatJpg, 200))
            .ToArray();
    }

    private sealed class DelegateProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}

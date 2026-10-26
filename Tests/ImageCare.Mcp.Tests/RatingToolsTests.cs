using System;
using System.Text.Json;

using ImageCare.Core.Domain.Annotations;
using ImageCare.Core.Domain.Configuration;
using ImageCare.Core.Services.FileSystemService;
using ImageCare.Core.Services.MediaAnnotationsService;
using ImageCare.Core.Services.MediaPreviewOperationsService;

using ImageCare.Mcp;
using ImageCare.Mcp.Media;
using ImageCare.Mcp.Security;
using ImageCare.Mcp.Sessions;
using ImageCare.Mcp.Tools;

using ModelContextProtocol;

using Moq;

using Serilog;

namespace ImageCare.Mcp.Tests;

[TestFixture]
public sealed class RatingToolsTests
{
    private string _directory = null!;
    private string _filePath = null!;
    private Mock<IMediaAnnotationsService> _annotations = null!;
    private RatingTools _tools = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = TestSupport.CreateTempDirectory();
        _filePath = TestSupport.CreateMediaFile(_directory, "frame.jpg", DateTime.UtcNow);

        _annotations = new Mock<IMediaAnnotationsService>();
        var logger = new LoggerConfiguration().CreateLogger();
        var options = new ImageCareMcpOptions
        {
            DataDirectory = Path.Combine(_directory, "mcp-data")
        };
        var mediaLibrary = new MediaLibrary(Mock.Of<IFileSystemService>(), logger);
        var sessionService = new ReviewSessionService(
            new ReviewSessionStore(options, logger),
            mediaLibrary,
            _annotations.Object,
            Mock.Of<IMediaPreviewOperationsService>(),
            options,
            logger);
        var policy = new McpPathPolicy(TestSupport.CreateConfigurationService(new Configuration
        {
            MediaSearchRootPaths = [_directory]
        }));

        _tools = new RatingTools(_annotations.Object, new McpPathGuard(policy, sessionService));
    }

    [TearDown]
    public void TearDown()
    {
        TestSupport.DeleteTempDirectory(_directory);
    }

    [Test]
    public async Task RatingSet_DryRun_ReportsChangeWithoutSaving()
    {
        var state = CreateState();
        _annotations
            .Setup(service => service.GetSnapshotAsync(_filePath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MediaAnnotationsSnapshot(
                _filePath,
                new MediaAnnotations(2, ["bird"]),
                state));

        var json = await _tools.RatingSet([_filePath], 5);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Multiple(() =>
        {
            Assert.That(root.GetProperty("dryRun").GetBoolean(), Is.True);
            Assert.That(root.GetProperty("rating").GetInt32(), Is.EqualTo(5));
            Assert.That(root.GetProperty("changed").GetInt32(), Is.EqualTo(1));
            Assert.That(root.GetProperty("results")[0].GetProperty("previousRating").GetInt32(), Is.EqualTo(2));
        });
        _annotations.Verify(
            service => service.SaveAsync(
                It.IsAny<string>(),
                It.IsAny<MediaAnnotations>(),
                It.IsAny<AnnotationFileState?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task RatingSet_PreservesExistingTagsAndExpectedState()
    {
        var state = CreateState();
        _annotations
            .Setup(service => service.GetSnapshotAsync(_filePath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MediaAnnotationsSnapshot(
                _filePath,
                new MediaAnnotations(2, ["bird", "lake"]),
                state));
        _annotations
            .Setup(service => service.SaveAsync(
                _filePath,
                It.IsAny<MediaAnnotations>(),
                state,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MediaAnnotationsSnapshot(
                _filePath,
                new MediaAnnotations(5, ["bird", "lake"]),
                state));

        await _tools.RatingSet([_filePath], 5, dryRun: false);

        _annotations.Verify(
            service => service.SaveAsync(
                _filePath,
                It.Is<MediaAnnotations>(annotations =>
                    annotations.Rating == 5 &&
                    annotations.Tags.Length == 2 &&
                    annotations.Tags.Contains("bird") &&
                    annotations.Tags.Contains("lake")),
                It.Is<AnnotationFileState?>(actual => actual == state),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task RatingSet_WhenRatingIsUnchanged_DoesNotSave()
    {
        _annotations
            .Setup(service => service.GetSnapshotAsync(_filePath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MediaAnnotationsSnapshot(
                _filePath,
                new MediaAnnotations(4, ["bird"]),
                CreateState()));

        var json = await _tools.RatingSet([_filePath], 4, dryRun: false);

        using var document = JsonDocument.Parse(json);
        Assert.That(document.RootElement.GetProperty("unchanged").GetInt32(), Is.EqualTo(1));
        _annotations.Verify(
            service => service.SaveAsync(
                It.IsAny<string>(),
                It.IsAny<MediaAnnotations>(),
                It.IsAny<AnnotationFileState?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task RatingSet_WhenFileIsMissing_ReturnsFailure()
    {
        var missingPath = Path.Combine(_directory, "missing.jpg");

        var json = await _tools.RatingSet([missingPath], 5, dryRun: false);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Multiple(() =>
        {
            Assert.That(root.GetProperty("failed").GetInt32(), Is.EqualTo(1));
            Assert.That(root.GetProperty("results")[0].GetProperty("error").GetString(), Is.EqualTo("file not found"));
        });
    }

    [Test]
    public void RatingSet_WhenRatingIsOutOfRange_Throws()
    {
        Assert.ThrowsAsync<McpException>(() => _tools.RatingSet([_filePath], 6));
        Assert.ThrowsAsync<McpException>(() => _tools.RatingSet([_filePath], -1));
    }

    private AnnotationFileState CreateState() => new(
        _filePath,
        StorageExists: false,
        StorageLength: 0,
        StorageModifiedUtc: DateTime.UtcNow,
        MediaLength: 16,
        MediaModifiedUtc: DateTime.UtcNow);
}

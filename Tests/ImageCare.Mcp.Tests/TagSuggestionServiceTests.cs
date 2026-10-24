using System;

using ImageCare.Core.Domain.Annotations;
using ImageCare.Core.Domain.Configuration;
using ImageCare.Core.Services.FileSystemService;
using ImageCare.Core.Services.MediaAnnotationsService;
using ImageCare.Core.Services.MediaPreviewOperationsService;

using ImageCare.Mcp;
using ImageCare.Mcp.Media;
using ImageCare.Mcp.Sessions;
using ImageCare.Mcp.Tags;

using Moq;

using Serilog;

namespace ImageCare.Mcp.Tests;

[TestFixture]
public sealed class TagSuggestionServiceTests
{
    private string _directory = null!;
    private string _dataDirectory = null!;
    private Configuration _configuration = null!;
    private Mock<IMediaAnnotationsService> _annotations = null!;
    private Mock<IFolderTagApplicationService> _folderTags = null!;
    private MediaLibrary _mediaLibrary = null!;
    private ReviewSessionService _sessionService = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = TestSupport.CreateTempDirectory();
        _dataDirectory = Path.Combine(_directory, "mcp-data");
        TestSupport.CreateMediaFile(_directory, "frame-1.jpg", DateTime.UtcNow.AddHours(-2));
        TestSupport.CreateMediaFile(_directory, "frame-2.jpg", DateTime.UtcNow.AddHours(-1));

        _configuration = new Configuration
        {
            MediaSearchRootPaths = [_directory],
            PinnedTags = ["новый-тег"]
        };

        _annotations = new Mock<IMediaAnnotationsService>();
        _annotations
            .Setup(service => service.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MediaAnnotations(0, []));
        _annotations
            .Setup(service => service.GetDirectoryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _folderTags = new Mock<IFolderTagApplicationService>();
        _folderTags
            .Setup(service => service.GetEffectiveTags(It.IsAny<string>()))
            .Returns(["птицы"]);

        var logger = new LoggerConfiguration().CreateLogger();
        var options = new ImageCareMcpOptions { DataDirectory = _dataDirectory };
        _mediaLibrary = new MediaLibrary(Mock.Of<IFileSystemService>(), logger);
        _sessionService = new ReviewSessionService(
            new ReviewSessionStore(options, logger),
            _mediaLibrary,
            _annotations.Object,
            Mock.Of<IMediaPreviewOperationsService>(),
            options,
            logger);
    }

    [TearDown]
    public void TearDown()
    {
        TestSupport.DeleteTempDirectory(_directory);
    }

    [Test]
    public async Task ProposeFolderTags_DoesNotRepeatTagsAlreadyInTheDescriptor()
    {
        var service = CreateService();

        var proposal = await service.ProposeFolderTagsAsync(_directory, sessionId: null, limit: 10, CancellationToken.None);

        Assert.That(proposal.Existing, Is.EquivalentTo(new[] { "птицы" }));
        Assert.That(proposal.Suggested.Select(suggestion => suggestion.Tag), Does.Contain("новый-тег"));
        Assert.That(proposal.Suggested.Select(suggestion => suggestion.Tag), Does.Not.Contain("птицы"));
    }

    private TagSuggestionService CreateService()
    {
        return new TagSuggestionService(
            _annotations.Object,
            _folderTags.Object,
            TestSupport.CreateConfigurationService(_configuration),
            _sessionService,
            _mediaLibrary,
            new LoggerConfiguration().CreateLogger());
    }
}

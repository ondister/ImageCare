using ImageCare.Core.Domain;
using ImageCare.Core.Domain.Configuration;
using ImageCare.Core.Domain.MediaFormats;
using ImageCare.Core.Domain.Preview;
using ImageCare.Core.Exceptions;
using ImageCare.Core.Services.FileSystemService;
using ImageCare.Core.Services.MediaPreviewOperationsService;
using ImageCare.Core.Services.ProcessService;

using Moq;

namespace ImageCare.Core.Tests.Services.MediaPreviewOperationsService;

[TestFixture]
public class ExternalProcessLauncherTests
{
    private Mock<IFileSystemService> _fileSystemServiceMock;
    private Mock<IProcessService> _processServiceMock;
    private ExternalProcessLauncher _launcher;

    [SetUp]
    public void SetUp()
    {
        _fileSystemServiceMock = new Mock<IFileSystemService>();
        _processServiceMock = new Mock<IProcessService>();
        _launcher = new ExternalProcessLauncher(
            _fileSystemServiceMock.Object,
            _processServiceMock.Object);
    }

    [Test]
    public void Open_WithExecutable_StartsProcessWithQuotedMediaPath()
    {
        var mediaPreview = CreateMediaPreview("folder/image.jpg");
        _fileSystemServiceMock.Setup(fileSystem => fileSystem.FileExists("editor.exe")).Returns(true);
        _fileSystemServiceMock.Setup(fileSystem => fileSystem.FileExists(mediaPreview.Url)).Returns(true);

        _launcher.Open(mediaPreview, "editor.exe");

        _processServiceMock.Verify(
            process => process.StartProcess("editor.exe", "\"folder/image.jpg\""),
            Times.Once);
    }

    [Test]
    public void Open_WithExecutable_ValidatesExecutableBeforeMedia()
    {
        var mediaPreview = CreateMediaPreview("missing.jpg");
        _fileSystemServiceMock.Setup(fileSystem => fileSystem.FileExists("missing.exe")).Returns(false);

        var exception = Assert.Throws<ServiceException>(() =>
            _launcher.Open(mediaPreview, "missing.exe"));

        Assert.That(exception!.Message, Is.EqualTo("Executable not found: missing.exe"));
        _fileSystemServiceMock.Verify(
            fileSystem => fileSystem.FileExists(mediaPreview.Url),
            Times.Never);
    }

    [Test]
    public void Open_WithExecutable_WhenProcessFails_WrapsException()
    {
        var mediaPreview = CreateMediaPreview("image.jpg");
        var processFailure = new InvalidOperationException("process failed");
        _fileSystemServiceMock
            .Setup(fileSystem => fileSystem.FileExists(It.IsAny<string>()))
            .Returns(true);
        _processServiceMock
            .Setup(process => process.StartProcess("editor.exe", "\"image.jpg\""))
            .Throws(processFailure);

        var exception = Assert.Throws<ServiceException>(() =>
            _launcher.Open(mediaPreview, "editor.exe"));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Is.EqualTo("Failed to open in external process"));
            Assert.That(exception.InnerException, Is.SameAs(processFailure));
        });
    }

    [TestCase(ApplicationLaunchMode.SystemDefault)]
    [TestCase(ApplicationLaunchMode.FileManager)]
    public void Open_WithSystemMode_DoesNotValidateApplicationPath(
        ApplicationLaunchMode launchMode)
    {
        var mediaPreview = CreateMediaPreview("image.jpg");
        var application = new FileApplicationInfo("Application", string.Empty, launchMode);
        _fileSystemServiceMock.Setup(fileSystem => fileSystem.FileExists(mediaPreview.Url)).Returns(true);

        _launcher.Open(mediaPreview, application);

        _processServiceMock.Verify(
            process => process.OpenFile(mediaPreview.Url, launchMode, string.Empty),
            Times.Once);
        _fileSystemServiceMock.Verify(
            fileSystem => fileSystem.DirectoryExists(It.IsAny<string>()),
            Times.Never);
    }

    [Test]
    public void Open_WithExecutableApplication_RequiresApplicationFile()
    {
        var mediaPreview = CreateMediaPreview("image.jpg");
        var application = new FileApplicationInfo(
            "Editor",
            "missing.exe",
            ApplicationLaunchMode.Executable);
        _fileSystemServiceMock.Setup(fileSystem => fileSystem.FileExists(mediaPreview.Url)).Returns(true);
        _fileSystemServiceMock.Setup(fileSystem => fileSystem.FileExists(application.ApplicationPath)).Returns(false);

        var exception = Assert.Throws<ServiceException>(() =>
            _launcher.Open(mediaPreview, application));

        Assert.That(exception!.Message, Is.EqualTo("Executable not found: missing.exe"));
    }

    [Test]
    public void Open_WithMacApplication_RequiresApplicationDirectory()
    {
        var mediaPreview = CreateMediaPreview("image.jpg");
        var application = new FileApplicationInfo(
            "Editor",
            "/Applications/Missing.app",
            ApplicationLaunchMode.MacApplication);
        _fileSystemServiceMock.Setup(fileSystem => fileSystem.FileExists(mediaPreview.Url)).Returns(true);
        _fileSystemServiceMock
            .Setup(fileSystem => fileSystem.DirectoryExists(application.ApplicationPath))
            .Returns(false);

        var exception = Assert.Throws<ServiceException>(() =>
            _launcher.Open(mediaPreview, application));

        Assert.That(
            exception!.Message,
            Is.EqualTo("Application not found: /Applications/Missing.app"));
    }

    [Test]
    public void Open_WithApplication_WhenOpenFails_WrapsException()
    {
        var mediaPreview = CreateMediaPreview("image.jpg");
        var application = new FileApplicationInfo(
            "System default",
            string.Empty,
            ApplicationLaunchMode.SystemDefault);
        var processFailure = new InvalidOperationException("open failed");
        _fileSystemServiceMock.Setup(fileSystem => fileSystem.FileExists(mediaPreview.Url)).Returns(true);
        _processServiceMock
            .Setup(process => process.OpenFile(
                mediaPreview.Url,
                application.LaunchMode,
                application.ApplicationPath))
            .Throws(processFailure);

        var exception = Assert.Throws<ServiceException>(() =>
            _launcher.Open(mediaPreview, application));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Is.EqualTo("Failed to open in external application"));
            Assert.That(exception.InnerException, Is.SameAs(processFailure));
        });
    }

    [TestCase(ApplicationLaunchMode.Executable)]
    [TestCase(ApplicationLaunchMode.MacApplication)]
    public void Open_WithConfiguredApplication_ValidatesPathAndForwards(
        ApplicationLaunchMode launchMode)
    {
        var mediaPreview = CreateMediaPreview("image.jpg");
        var applicationPath = launchMode == ApplicationLaunchMode.Executable
            ? "editor.exe"
            : "/Applications/Editor.app";
        var application = new FileApplicationInfo("Editor", applicationPath, launchMode);
        _fileSystemServiceMock.Setup(fileSystem => fileSystem.FileExists(mediaPreview.Url)).Returns(true);
        _fileSystemServiceMock
            .Setup(fileSystem => fileSystem.FileExists(applicationPath))
            .Returns(launchMode == ApplicationLaunchMode.Executable);
        _fileSystemServiceMock
            .Setup(fileSystem => fileSystem.DirectoryExists(applicationPath))
            .Returns(launchMode == ApplicationLaunchMode.MacApplication);

        _launcher.Open(mediaPreview, application);

        _processServiceMock.Verify(
            process => process.OpenFile(mediaPreview.Url, launchMode, applicationPath),
            Times.Once);
    }

    [Test]
    public void Open_WithNullApplication_WrapsArgumentNullException()
    {
        var exception = Assert.Throws<ServiceException>(() =>
            _launcher.Open(CreateMediaPreview("image.jpg"), (FileApplicationInfo)null!));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Is.EqualTo("Failed to open in external application"));
            Assert.That(exception.InnerException, Is.TypeOf<ArgumentNullException>());
        });
    }

    private static MediaPreview CreateMediaPreview(string path) =>
        new("Test", path, MediaFormat.MediaFormatJpg, 800);
}

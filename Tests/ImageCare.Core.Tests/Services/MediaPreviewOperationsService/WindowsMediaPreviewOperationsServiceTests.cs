using ImageCare.Core.Domain.Folders;
using ImageCare.Core.Domain.MediaFormats;
using ImageCare.Core.Domain.Preview;
using ImageCare.Core.Exceptions;
using ImageCare.Core.Services.FileSystemService;
using ImageCare.Core.Services.MediaPreviewOperationsService;
using ImageCare.Core.Services.ProcessService;

using Moq;

namespace ImageCare.Core.Tests.Services.MediaPreviewOperationsService;

[TestFixture]
public class WindowsMediaPreviewOperationsServiceTests
{
    private Mock<IFileSystemService> _fileSystemServiceMock;
    private Mock<IProcessService> _processServiceMock;
    private MediaFormat _testMediaFormat;

    [SetUp]
    public void SetUp()
    {
        _fileSystemServiceMock = new Mock<IFileSystemService>();
        _processServiceMock = new Mock<IProcessService>();
        _testMediaFormat = MediaFormat.MediaFormatJpg;
    }

    [Test]
    public void SetSelectedPreview_UpdatesLastSelectedAndNotifiesObservers()
    {
        var service = CreateService();
        var selectedPreview = CreateSelectedMediaPreview();

        SelectedMediaPreview? notifiedPreview = null;
        service.ImagePreviewSelected.Subscribe(preview => notifiedPreview = preview);

        service.SetSelectedPreview(selectedPreview);

        Assert.That(service.GetLastSelectedMediaPreview(), Is.EqualTo(selectedPreview));
        Assert.That(notifiedPreview, Is.EqualTo(selectedPreview));
    }

    [Test]
    public void CopyImagePreviewToDirectoryAsync_WhenDirectoryNotExists_ThrowsException()
    {
        var service = CreateService();
        var mediaPreview = CreateMediaPreview();

        _fileSystemServiceMock.Setup(x => x.DirectoryExists("targetDir")).Returns(false);

        Assert.That(() => service.CopyImagePreviewToDirectoryAsync(mediaPreview, "targetDir", new Progress<OperationInfo>()),
            Throws.TypeOf<ServiceException>());
    }

    [Test]
    public void CopyImagePreviewToDirectoryAsync_WhenDirectoryExists_CopiesFile()
    {
        var service = CreateService();
        var mediaPreview = CreateMediaPreview("source.jpg");
        var progress = new Progress<OperationInfo>();

        _fileSystemServiceMock.Setup(x => x.DirectoryExists("targetDir")).Returns(true);
        _fileSystemServiceMock.Setup(x => x.GetFiles("targetDir")).Returns(Array.Empty<string>());
        _fileSystemServiceMock.Setup(x => x.FileExists("source.jpg")).Returns(true);
        _fileSystemServiceMock.Setup(x => x.GetFileInfo("source.jpg")).Returns(new FileInfo("source.jpg"));
        _fileSystemServiceMock.Setup(x => x.OpenRead("source.jpg")).Returns(new MemoryStream());
        _fileSystemServiceMock.Setup(x => x.Create(It.IsAny<string>())).Returns(new MemoryStream());

        var result = service.CopyImagePreviewToDirectoryAsync(mediaPreview, "targetDir", progress).Result;

        Assert.That(result, Is.EqualTo(OperationResult.Success));
    }

    [Test]
    public void DeleteImagePreviewAsync_WhenFileExists_DeletesFile()
    {
        var service = CreateService();
        var mediaPreview = CreateMediaPreview("existing.jpg");

        _fileSystemServiceMock.Setup(x => x.FileExists("existing.jpg")).Returns(true);

        var result = service.DeleteImagePreviewAsync(mediaPreview).Result;

        Assert.That(result, Is.EqualTo(OperationResult.Success));
        _fileSystemServiceMock.Verify(x => x.SafeDelete("existing.jpg"), Times.Once);
    }

    [Test]
    public void OpenInExternalProcess_WhenExecutableNotExists_ThrowsServiceException()
    {
        var service = CreateService();
        var mediaPreview = CreateMediaPreview("image.jpg");

        _fileSystemServiceMock.Setup(x => x.FileExists("app.exe")).Returns(false);

        Assert.That(() => service.OpenInExternalProcess(mediaPreview, "app.exe"),
            Throws.TypeOf<ServiceException>());
    }

    [Test]
    public void OpenInExternalProcess_WhenMediaFileNotExists_ThrowsServiceException()
    {
        var service = CreateService();
        var mediaPreview = CreateMediaPreview("image.jpg");

        _fileSystemServiceMock.Setup(x => x.FileExists("app.exe")).Returns(true);
        _fileSystemServiceMock.Setup(x => x.FileExists("image.jpg")).Returns(false);

        Assert.That(() => service.OpenInExternalProcess(mediaPreview, "app.exe"),
            Throws.TypeOf<ServiceException>());
    }

    [Test]
    public void OpenInExternalProcess_WithValidFiles_StartsProcess()
    {
        var service = CreateService();
        var mediaPreview = CreateMediaPreview("image.jpg");

        _fileSystemServiceMock.Setup(x => x.FileExists("app.exe")).Returns(true);
        _fileSystemServiceMock.Setup(x => x.FileExists("image.jpg")).Returns(true);

        service.OpenInExternalProcess(mediaPreview, "app.exe");

        _processServiceMock.Verify(x => x.StartProcess("app.exe", "\"image.jpg\""), Times.Once);
    }

    [Test]
    public void GetLastSelectedMediaPreview_Initially_ReturnsNull()
    {
        var service = CreateService();

        var result = service.GetLastSelectedMediaPreview();

        Assert.That(result, Is.Null);
    }

    [Test]
    public void GetLastSelectedMediaPreview_AfterSetting_ReturnsSelectedPreview()
    {
        var service = CreateService();
        var selectedPreview = CreateSelectedMediaPreview();

        service.SetSelectedPreview(selectedPreview);
        var result = service.GetLastSelectedMediaPreview();

        Assert.That(result, Is.EqualTo(selectedPreview));
    }

    [Test]
    public void Dispose_DisposesSubject()
    {
        var service = CreateService();

        service.Dispose();

        Assert.That(() => service.ImagePreviewSelected.Subscribe(_ => { }),
            Throws.TypeOf<ObjectDisposedException>());
    }

    [Test]
    public void MoveImagePreviewToDirectoryAsync_WhenDirectoryNotExists_ThrowsException()
    {
        var service = CreateService();
        var mediaPreview = CreateMediaPreview();

        _fileSystemServiceMock.Setup(x => x.DirectoryExists("targetDir")).Returns(false);

        Assert.That(() => service.MoveImagePreviewToDirectoryAsync(mediaPreview, "targetDir", new Progress<OperationInfo>()),
            Throws.TypeOf<ServiceException>());
    }

    [Test]
    public void SetSelectedPreview_WithDifferentPanels_StoresSeparately()
    {
        var service = CreateService();
        var leftPreview = CreateSelectedMediaPreview();
        var rightPreview = CreateSelectedMediaPreview(FileManagerPanel.Right);

        service.SetSelectedPreview(leftPreview);
        service.SetSelectedPreview(rightPreview);

        Assert.That(service.GetLastSelectedMediaPreview(), Is.EqualTo(rightPreview));
    }

    private WindowsMediaPreviewOperationsService CreateService()
    {
        return new WindowsMediaPreviewOperationsService(
            _fileSystemServiceMock.Object,
            _processServiceMock.Object);
    }

    private MediaPreview CreateMediaPreview(string url = "test.jpg")
    {
        return new MediaPreview("Test Title", url, _testMediaFormat, 800);
    }

    private SelectedMediaPreview CreateSelectedMediaPreview(FileManagerPanel panel = FileManagerPanel.Left)
    {
        return new SelectedMediaPreview(CreateMediaPreview(), panel);
    }
}
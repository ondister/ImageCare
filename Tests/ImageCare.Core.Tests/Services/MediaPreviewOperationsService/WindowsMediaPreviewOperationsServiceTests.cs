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

		Assert.AreEqual(selectedPreview, service.GetLastSelectedMediaPreview());
		Assert.AreEqual(selectedPreview, notifiedPreview);
	}

	[Test]
	public void CopyImagePreviewToDirectoryAsync_WhenDirectoryNotExists_ReturnsFailed()
	{
		var service = CreateService();
		var mediaPreview = CreateMediaPreview();

		_fileSystemServiceMock.Setup(x => x.DirectoryExists("targetDir")).Returns(false);

		var result = service.CopyImagePreviewToDirectoryAsync(mediaPreview, "targetDir", new Progress<OperationInfo>()).Result;

		Assert.AreEqual(OperationResult.Failed, result);
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

		Assert.AreEqual(OperationResult.Success, result);
	}

	[Test]
	public void DeleteImagePreviewAsync_WhenFileNotExists_ReturnsFailed()
	{
		var service = CreateService();
		var mediaPreview = CreateMediaPreview("nonexistent.jpg");

		_fileSystemServiceMock.Setup(x => x.FileExists("nonexistent.jpg")).Returns(false);

		var result = service.DeleteImagePreviewAsync(mediaPreview).Result;

		Assert.AreEqual(OperationResult.Failed, result);
	}

	[Test]
	public void DeleteImagePreviewAsync_WhenFileExists_DeletesFile()
	{
		var service = CreateService();
		var mediaPreview = CreateMediaPreview("existing.jpg");

		_fileSystemServiceMock.Setup(x => x.FileExists("existing.jpg")).Returns(true);

		var result = service.DeleteImagePreviewAsync(mediaPreview).Result;

		Assert.AreEqual(OperationResult.Success, result);
		_fileSystemServiceMock.Verify(x => x.SafeDelete("existing.jpg"), Times.Once);
	}

	[Test]
	public void OpenInExternalProcess_WhenExecutableNotExists_ThrowsServiceException()
	{
		var service = CreateService();
		var mediaPreview = CreateMediaPreview("image.jpg");

		_fileSystemServiceMock.Setup(x => x.FileExists("app.exe")).Returns(false);

		Assert.Throws<ServiceException>(() => service.OpenInExternalProcess(mediaPreview, "app.exe"));
	}

	[Test]
	public void OpenInExternalProcess_WhenMediaFileNotExists_ThrowsServiceException()
	{
		var service = CreateService();
		var mediaPreview = CreateMediaPreview("image.jpg");

		_fileSystemServiceMock.Setup(x => x.FileExists("app.exe")).Returns(true);
		_fileSystemServiceMock.Setup(x => x.FileExists("image.jpg")).Returns(false);

		Assert.Throws<ServiceException>(() => service.OpenInExternalProcess(mediaPreview, "app.exe"));
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

		Assert.IsNull(result);
	}

	[Test]
	public void GetLastSelectedMediaPreview_AfterSetting_ReturnsSelectedPreview()
	{
		var service = CreateService();
		var selectedPreview = CreateSelectedMediaPreview();

		service.SetSelectedPreview(selectedPreview);
		var result = service.GetLastSelectedMediaPreview();

		Assert.AreEqual(selectedPreview, result);
	}

	[Test]
	public void Dispose_DisposesSubject()
	{
		var service = CreateService();

		service.Dispose();

		Assert.Throws<ObjectDisposedException>(() =>
			                                       service.ImagePreviewSelected.Subscribe(_ => { }));
	}

	[Test]
	public void MoveImagePreviewToDirectoryAsync_WhenDirectoryNotExists_ReturnsFailed()
	{
		var service = CreateService();
		var mediaPreview = CreateMediaPreview();

		_fileSystemServiceMock.Setup(x => x.DirectoryExists("targetDir")).Returns(false);

		var result = service.MoveImagePreviewToDirectoryAsync(mediaPreview, "targetDir", new Progress<OperationInfo>()).Result;

		Assert.AreEqual(OperationResult.Failed, result);
	}

	[Test]
	public void SetSelectedPreview_WithDifferentPanels_StoresSeparately()
	{
		var service = CreateService();
		var leftPreview = CreateSelectedMediaPreview();
		var rightPreview = CreateSelectedMediaPreview(FileManagerPanel.Right);

		service.SetSelectedPreview(leftPreview);
		service.SetSelectedPreview(rightPreview);

		Assert.AreEqual(rightPreview, service.GetLastSelectedMediaPreview());
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
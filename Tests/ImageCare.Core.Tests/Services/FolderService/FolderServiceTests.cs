using ImageCare.Core.Domain.Folders;
using ImageCare.Core.Services.FileSystemService;
using ImageCare.Core.Services.FolderService;

using Moq;

namespace ImageCare.Core.Tests.Services.FolderService;

[TestFixture]
public class LocalFileSystemFolderServiceTests
{
	private Mock<IFileSystemService> _fileSystemServiceMock;
	private Mock<IDriveModelsFactory> _driveModelsFactoryMock;
	private LocalFileSystemFolderService _service;

	[SetUp]
	public void Setup()
	{
		_fileSystemServiceMock = new Mock<IFileSystemService>();
		_driveModelsFactoryMock = new Mock<IDriveModelsFactory>();
		_service = new LocalFileSystemFolderService(_fileSystemServiceMock.Object, _driveModelsFactoryMock.Object);
	}

	[TearDown]
	public void TearDown()
	{
		_service.Dispose();
	}

	[Test]
	public void Constructor_WithNullFileSystemService_ThrowsArgumentNullException()
	{
		Assert.Throws<ArgumentNullException>(() =>
			                                     new LocalFileSystemFolderService(null, _driveModelsFactoryMock.Object));
	}

	[Test]
	public void Constructor_WithNullDriveModelsFactory_ThrowsArgumentNullException()
	{
		Assert.Throws<ArgumentNullException>(() =>
			                                     new LocalFileSystemFolderService(_fileSystemServiceMock.Object, null));
	}

	[Test]
	public void GetDirectoryModelAsync_WithNullDirectoryModel_ReturnsRootDirectories()
	{
		var result = _service.GetDirectoryModelAsync().Result;

		Assert.That(result, Is.InstanceOf<DeviceModel>());
	}

	[Test]
	public void GetDirectoryModelAsync_WithInvalidPath_ReturnsInvalidFolderModel()
	{
		_fileSystemServiceMock.Setup(x => x.DirectoryExists(It.IsAny<string>())).Returns(false);

		var result = _service.GetDirectoryModelAsync("invalid_path").Result;

		Assert.That(result.Name, Is.EqualTo("Invalid folder"));
	}

	[Test]
	public void GetFileModelAsync_WithNonExistentDirectory_ReturnsEmptyCollection()
	{
		_fileSystemServiceMock.Setup(x => x.DirectoryExists(It.IsAny<string>())).Returns(false);

		var result = _service.GetFileModelAsync("nonexistent", "*.jpg").Result;

		Assert.That(result, Is.Empty);
	}

	[Test]
	public void SetSelectedDirectory_AddsToSelectedDirectories()
	{
		var directory = new DirectoryModel("test", "test_path");
		var selectedDir = new SelectedDirectory(directory, FileManagerPanel.Left);

		_service.SetSelectedDirectory(selectedDir);
		var result = _service.GetSelectedDirectory(FileManagerPanel.Left);

		Assert.That(result, Is.EqualTo(selectedDir));
	}

	[Test]
	public void AddVisitingFolder_EmitsFolderVisitedEvent()
	{
		var directory = new DirectoryModel("test", "test_path");
		SelectedDirectory capturedEvent = null;

		using var subscription = _service.FolderVisited.Subscribe(dir => capturedEvent = dir);
		_service.AddVisitingFolder(directory, FileManagerPanel.Left);

		Assert.That(capturedEvent, Is.Not.Null);
		Assert.That(capturedEvent.FileManagerPanel, Is.EqualTo(FileManagerPanel.Left));
	}

	[Test]
	public void RemoveVisitingFolder_EmitsFolderLeftEvent()
	{
		var directory = new DirectoryModel("test", "test_path");
		_service.AddVisitingFolder(directory, FileManagerPanel.Left);

		SelectedDirectory capturedEvent = null;
		using var subscription = _service.FolderLeft.Subscribe(dir => capturedEvent = dir);
		_service.RemoveVisitingFolder(directory, FileManagerPanel.Left);

		Assert.That(capturedEvent, Is.Not.Null);
	}

	[Test]
	public void CreateSubFolder_WithDeviceModel_ReturnsNull()
	{
		var deviceModel = new DeviceModel("device", "path");

		var result = _service.CreateSubFolder(deviceModel);

		Assert.That(result, Is.Null);
	}

	[Test]
	public void Dispose_MakesServiceUnusable()
	{
		_service.Dispose();

		Assert.Throws<ObjectDisposedException>(() =>
			                                       _service.SetSelectedDirectory(new SelectedDirectory("test", "path", FileManagerPanel.Left)));
	}
}
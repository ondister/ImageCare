using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Reflection;

using ImageCare.Core.Domain.Folders;
using ImageCare.Core.Services.DrivesWatcherService;
using ImageCare.Core.Services.FolderService;

using Moq;

namespace ImageCare.Core.Tests.Services.DrivesWatcherService;

[TestFixture]
public class WindowsDrivesWatcherServiceTests
{
	[Test]
	public void StartWatching_StartsManagementEventWatcher()
	{
		var watcherMock = new Mock<IManagementEventWatcher>();
		using var service = CreateService(watcherMock: watcherMock);

		service.StartWatching();

		watcherMock.Verify(x => x.Start(), Times.Once);
	}

	[Test]
	public void StopWatching_StopsManagementEventWatcher()
	{
		var watcherMock = new Mock<IManagementEventWatcher>();
		using var service = CreateService(watcherMock: watcherMock);
		service.StartWatching();

		service.StopWatching();

		watcherMock.Verify(x => x.Stop(), Times.Once);
	}

	[Test]
	public async Task DeviceArrivalObservable_WithValidDrive_PublishesDriveModel()
	{
		var deviceArrivedSubject = new Subject<string>();

		var driveInfo = CreateDriveInfo("D:", DriveType.Removable);
		var driveModel = new DriveModel("Removable", "D:\\");
		var directories = new DriveModel("dir", "D:\\dir");

		var driveInfoProviderMock = new Mock<IDriveInfoProvider>();
		var folderServiceMock = new Mock<IFolderService>();
		var driveModelsFactoryMock = new Mock<IDriveModelsFactory>();

		// Create a task completion source to signal when the drive is published
		var publishedTask = new TaskCompletionSource<DriveModel>();

		driveInfoProviderMock.Setup(x => x.GetDriveByName("D:")).Returns(driveInfo);
		driveModelsFactoryMock.Setup(x => x.CreateDriveModel(driveInfo)).Returns(driveModel);
		folderServiceMock.Setup(x => x.GetCustomDirectoriesLevelAsync(driveModel, true))
		                 .ReturnsAsync(directories);

		using var service = CreateService(
			folderServiceMock: folderServiceMock,
			driveInfoProviderMock: driveInfoProviderMock,
			driveModelsFactoryMock: driveModelsFactoryMock,
			deviceArrivedObservable: deviceArrivedSubject);

		service.StartWatching();

		// Subscribe and complete the task when drive is published
		using var subscription = service.DriveMounted.Subscribe(drive => { publishedTask.TrySetResult(drive); });

		deviceArrivedSubject.OnNext("D:");

		// Wait for the drive to be published with timeout
		var publishedDrive = await publishedTask.Task.WaitAsync(TimeSpan.FromSeconds(1));

		Assert.That(publishedDrive, Is.Not.Null);
		Assert.That(publishedDrive, Is.SameAs(driveModel));
		folderServiceMock.Verify(x => x.GetCustomDirectoriesLevelAsync(driveModel, true), Times.Once);
	}

	[Test]
	public void DeviceArrivalObservable_WithNonExistentDrive_DoesNotPublishDriveModel()
	{
		var driveInfoProviderMock = new Mock<IDriveInfoProvider>();
		var driveModelsFactoryMock = new Mock<IDriveModelsFactory>();

		driveInfoProviderMock.Setup(x => x.GetDriveByName("X:")).Returns((DriveInfo)null);

		var deviceArrivedSubject = new Subject<string>();

		using var service = CreateService(
			driveInfoProviderMock: driveInfoProviderMock,
			driveModelsFactoryMock: driveModelsFactoryMock,
			deviceArrivedObservable: deviceArrivedSubject);

		service.StartWatching();

		DriveModel publishedDrive = null;
		using var subscription = service.DriveMounted.Subscribe(drive => publishedDrive = drive);

		deviceArrivedSubject.OnNext("X:");

		Assert.That(publishedDrive, Is.Null);
		driveModelsFactoryMock.Verify(x => x.CreateDriveModel(It.IsAny<DriveInfo>()), Times.Never);
	}

	[Test]
	public void DeviceRemovalObservable_WithRemovedDrive_PublishesUnmountEvent()
	{
		var driveInfoProviderMock = new Mock<IDriveInfoProvider>();
		driveInfoProviderMock.Setup(x => x.GetDriveByName("E:")).Returns((DriveInfo)null);

		var deviceRemovedSubject = new Subject<string>();

		using var service = CreateService(
			driveInfoProviderMock: driveInfoProviderMock,
			deviceRemovedObservable: deviceRemovedSubject);

		service.StartWatching();

		string unmountedDrive = null;
		using var subscription = service.DriveUnmounted.Subscribe(drive => unmountedDrive = drive);

		deviceRemovedSubject.OnNext("E:");

		Assert.That(unmountedDrive, Is.EqualTo("E:\\"));
	}

	[Test]
	public void DeviceRemovalObservable_WithExistingDrive_DoesNotPublishUnmountEvent()
	{
		var driveInfo = CreateDriveInfo("F:", DriveType.Removable);
		var driveInfoProviderMock = new Mock<IDriveInfoProvider>();
		driveInfoProviderMock.Setup(x => x.GetDriveByName("F:")).Returns(driveInfo);

		var deviceRemovedSubject = new Subject<string>();

		using var service = CreateService(
			driveInfoProviderMock: driveInfoProviderMock,
			deviceRemovedObservable: deviceRemovedSubject);

		service.StartWatching();

		string unmountedDrive = null;
		using var subscription = service.DriveUnmounted.Subscribe(drive => unmountedDrive = drive);

		deviceRemovedSubject.OnNext("F:");

		Assert.That(unmountedDrive, Is.Null);
	}

	[Test]
	public void DeviceArrivalObservable_WithFolderServiceError_DoesNotPropagateException()
	{
		var driveInfo = CreateDriveInfo("H:", DriveType.Removable);
		var driveModel = new DriveModel("Removable", "H:\\");

		var driveInfoProviderMock = new Mock<IDriveInfoProvider>();
		var folderServiceMock = new Mock<IFolderService>();
		var driveModelsFactoryMock = new Mock<IDriveModelsFactory>();

		driveInfoProviderMock.Setup(x => x.GetDriveByName("H:")).Returns(driveInfo);
		driveModelsFactoryMock.Setup(x => x.CreateDriveModel(driveInfo)).Returns(driveModel);
		folderServiceMock.Setup(x => x.GetCustomDirectoriesLevelAsync(driveModel, true))
		                 .ThrowsAsync(new InvalidOperationException());

		var deviceArrivedSubject = new Subject<string>();

		using var service = CreateService(
			folderServiceMock: folderServiceMock,
			driveInfoProviderMock: driveInfoProviderMock,
			driveModelsFactoryMock: driveModelsFactoryMock,
			deviceArrivedObservable: deviceArrivedSubject);

		service.StartWatching();

		DriveModel publishedDrive = null;
		using var subscription = service.DriveMounted.Subscribe(drive => publishedDrive = drive);

		Assert.DoesNotThrow(() => deviceArrivedSubject.OnNext("H:"));
		Assert.That(publishedDrive, Is.Null);
	}

	[Test]
	public void DeviceArrivalObservable_WhenFactoryReturnsNull_DoesNotPublishDriveModel()
	{
		var driveInfo = CreateDriveInfo("I:", DriveType.Removable);

		var driveInfoProviderMock = new Mock<IDriveInfoProvider>();
		var driveModelsFactoryMock = new Mock<IDriveModelsFactory>();
		var folderServiceMock = new Mock<IFolderService>();

		driveInfoProviderMock.Setup(x => x.GetDriveByName("I:")).Returns(driveInfo);
		driveModelsFactoryMock.Setup(x => x.CreateDriveModel(driveInfo)).Returns((DriveModel)null);

		var deviceArrivedSubject = new Subject<string>();

		using var service = CreateService(
			driveInfoProviderMock: driveInfoProviderMock,
			driveModelsFactoryMock: driveModelsFactoryMock,
			folderServiceMock: folderServiceMock,
			deviceArrivedObservable: deviceArrivedSubject);

		service.StartWatching();

		DriveModel publishedDrive = null;
		using var subscription = service.DriveMounted.Subscribe(drive => publishedDrive = drive);

		deviceArrivedSubject.OnNext("I:");

		Assert.That(publishedDrive, Is.Null);
		folderServiceMock.Verify(x => x.GetCustomDirectoriesLevelAsync(It.IsAny<DriveModel>(), true), Times.Never);
	}


	[Test]
	public void StartWatching_AfterDispose_ThrowsObjectDisposedException()
	{
		var service = CreateService();
		service.Dispose();

		Assert.Throws<ObjectDisposedException>(() => service.StartWatching());
	}

	[Test]
	public void Dispose_StopsAllComponents()
	{
		var watcherMock = new Mock<IManagementEventWatcher>();
		var service = CreateService(watcherMock: watcherMock);
		service.StartWatching();

		service.Dispose();

		watcherMock.Verify(x => x.Dispose(), Times.Once);
	}

	private WindowsDrivesWatcherService CreateService(Mock<IFolderService>? folderServiceMock = null,
	                                                  Mock<IManagementEventWatcher>? watcherMock = null,
	                                                  Mock<IDriveInfoProvider>? driveInfoProviderMock = null,
	                                                  Mock<IDriveModelsFactory>? driveModelsFactoryMock = null,
	                                                  IObservable<string>? deviceArrivedObservable = null,
	                                                  IObservable<string>? deviceRemovedObservable = null)
	{
		folderServiceMock ??= new Mock<IFolderService>();
		driveInfoProviderMock ??= new Mock<IDriveInfoProvider>();
		driveModelsFactoryMock ??= new Mock<IDriveModelsFactory>();

		watcherMock ??= new Mock<IManagementEventWatcher>();
		watcherMock.Setup(x => x.DeviceArrived)
		           .Returns(deviceArrivedObservable ?? Observable.Never<string>());
		watcherMock.Setup(x => x.DeviceRemoved)
		           .Returns(deviceRemovedObservable ?? Observable.Never<string>());

		return new WindowsDrivesWatcherService(
			folderServiceMock.Object,
			watcherMock.Object,
			driveInfoProviderMock.Object,
			driveModelsFactoryMock.Object);
	}

	private static DriveInfo CreateDriveInfo(string name, DriveType driveType, long freeSpace = 1000000)
	{
		var driveInfo = new DriveInfo(name);

		SetDriveType(driveInfo, driveType);
		SetAvailableFreeSpace(driveInfo, freeSpace);

		return driveInfo;
	}

	private static void SetDriveType(DriveInfo driveInfo, DriveType driveType)
	{
		var driveTypeField = typeof(DriveInfo).GetField("_driveType", BindingFlags.NonPublic | BindingFlags.Instance);
		driveTypeField?.SetValue(driveInfo, driveType);
	}

	private static void SetAvailableFreeSpace(DriveInfo driveInfo, long freeSpace)
	{
		var freeSpaceField = typeof(DriveInfo).GetField("_availableFreeSpace", BindingFlags.NonPublic | BindingFlags.Instance);
		freeSpaceField?.SetValue(driveInfo, freeSpace);
	}
}
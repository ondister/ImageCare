using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;

using ImageCare.Core.Domain.Folders;
using ImageCare.Core.Services.FolderService;

namespace ImageCare.Core.Services.DrivesWatcherService;

public sealed class WindowsDrivesWatcherService : IDrivesWatcherService, IDisposable
{
	private readonly IFolderService _folderService;
	private readonly IManagementEventWatcher _watcher;
	private readonly IDriveInfoProvider _driveInfoProvider;
	private readonly IDriveModelsFactory _driveModelsFactory;

	private readonly Subject<DriveModel> _driveMountedSubject;
	private readonly Subject<string> _driveUnmountedSubject;
	private readonly Subject<AvailableFreeSpaceInfo> _driveFreeSpaceSubject;
	private readonly CompositeDisposable _subscriptions;

	private CancellationTokenSource _cancellationTokenSource;
	private bool _disposed;

	public WindowsDrivesWatcherService(IFolderService folderService,
	                                   IManagementEventWatcher watcher,
	                                   IDriveInfoProvider driveInfoProvider,
	                                   IDriveModelsFactory driveModelsFactory)
	{
		_folderService = folderService;
		_watcher = watcher;
		_driveInfoProvider = driveInfoProvider;
		_driveModelsFactory = driveModelsFactory;

		_driveMountedSubject = new Subject<DriveModel>();
		_driveUnmountedSubject = new Subject<string>();
		_driveFreeSpaceSubject = new Subject<AvailableFreeSpaceInfo>();

		_subscriptions = new CompositeDisposable
		{
			_watcher.DeviceArrived.Select(driveName => Observable.FromAsync(() => OnDeviceArrival(driveName))).Concat().Subscribe(),
			_watcher.DeviceRemoved.Subscribe(OnDeviceRemoval)
		};

		_cancellationTokenSource = new CancellationTokenSource();
	}

	public IObservable<DriveModel> DriveMounted => _driveMountedSubject.AsObservable();

	public IObservable<string> DriveUnmounted => _driveUnmountedSubject.AsObservable();

	public IObservable<AvailableFreeSpaceInfo> DriveAvailableFreeSpaceChanged => _driveFreeSpaceSubject.AsObservable();

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_watcher.Dispose();
		_cancellationTokenSource?.Cancel();
		_cancellationTokenSource?.Dispose();
		_driveMountedSubject.Dispose();
		_driveUnmountedSubject.Dispose();
		_driveFreeSpaceSubject.Dispose();
		_subscriptions.Dispose();
		_disposed = true;
	}

	public void StartWatching()
	{
		if (_disposed)
		{
			throw new ObjectDisposedException(nameof(WindowsDrivesWatcherService));
		}

		_watcher.Start();
		_cancellationTokenSource = new CancellationTokenSource();
		_ = StartWatchingFreeSpaceAsync(_cancellationTokenSource.Token);
	}

	public void StopWatching()
	{
		_watcher.Stop();
		_cancellationTokenSource?.Cancel();
	}

	private async Task OnDeviceArrival(string driveName)
	{
		var driveInfo = _driveInfoProvider.GetDriveByName(driveName);
		if (driveInfo == null)
		{
			return;
		}

		var driveModel = _driveModelsFactory.CreateDriveModel(driveInfo);
		if (driveModel == null)
		{
			return;
		}

		try
		{
			var directories = await _folderService.GetCustomDirectoriesLevelAsync(driveModel, true);
			driveModel.AddDirectories(directories.DirectoryModels);
			_driveMountedSubject.OnNext(driveModel);
		}
		catch
		{
			//Ignored
		}
	}

	private void OnDeviceRemoval(string driveName)
	{
		var driveInfo = _driveInfoProvider.GetDriveByName(driveName);
		if (driveInfo == null)
		{
			_driveUnmountedSubject.OnNext($"{driveName}\\");
		}
	}

	private async Task StartWatchingFreeSpaceAsync(CancellationToken token)
	{
		while (!token.IsCancellationRequested)
		{
			try
			{
				var drives = _driveInfoProvider.GetDrives()
				                               .Where(d => d is { DriveType: DriveType.Removable, IsReady: true });

				foreach (var drive in drives)
				{
					_driveFreeSpaceSubject.OnNext(
						new AvailableFreeSpaceInfo(
							drive.RootDirectory.FullName,
							drive.AvailableFreeSpace));
				}

				await Task.Delay(TimeSpan.FromSeconds(5), token);
			}
			catch (OperationCanceledException)
			{
				break;
			}
		}
	}
}
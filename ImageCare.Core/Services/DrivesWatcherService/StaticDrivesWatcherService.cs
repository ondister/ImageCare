using System.Reactive.Linq;

using ImageCare.Core.Domain.Folders;

namespace ImageCare.Core.Services.DrivesWatcherService;

public sealed class StaticDrivesWatcherService : IDrivesWatcherService
{
    public IObservable<DriveModel> DriveMounted { get; } = Observable.Never<DriveModel>();

    public IObservable<string> DriveUnmounted { get; } = Observable.Never<string>();

    public IObservable<AvailableFreeSpaceInfo> DriveAvailableFreeSpaceChanged { get; } =
        Observable.Never<AvailableFreeSpaceInfo>();

    public void StartWatching()
    {
    }

    public void StopWatching()
    {
    }
}

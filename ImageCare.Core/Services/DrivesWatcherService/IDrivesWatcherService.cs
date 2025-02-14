using ImageCare.Core.Domain.Folders;

namespace ImageCare.Core.Services.DrivesWatcherService;

public interface IDrivesWatcherService
{
    IObservable<DriveModel> DriveMounted { get; }

    IObservable<string> DriveUnmounted { get; }

    IObservable<AvailableFreeSpaceInfo> DriveAvailableFreeSpaceChanged { get; }

    void StartWatching();

    void StopWatching();
}
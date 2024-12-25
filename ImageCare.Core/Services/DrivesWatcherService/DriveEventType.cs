namespace ImageCare.Core.Services.DrivesWatcherService;

internal enum DriveEventType : ushort
{
    ConfigurationChanged = 1,
    DeviceArrival = 2,
    DeviceRemoval = 3,
    Docking = 4
}
namespace ImageCare.Core.Services.FolderStatisticsService;

public enum ScanStatus
{
    Idle,
    InitialScanStarted,
    InitialScanCompleted,
    FileChanged,
    ErrorOccurred
}
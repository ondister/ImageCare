namespace ImageCare.Core.Services.FolderStatisticsService;

public record ScanProgress(
    ScanStatus Status,
    int ProcessedFiles = 0,
    int BucketsCount = 0,
    int TotalFiles = 0,
    Exception? Error = null);
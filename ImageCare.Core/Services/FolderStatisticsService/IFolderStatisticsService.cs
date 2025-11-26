using ImageCare.Core.Domain.Folders;

namespace ImageCare.Core.Services.FolderStatisticsService;

public interface IFolderStatisticsService : IDisposable
{
    IObservable<FilesBucket> BucketChanged { get; }

    IObservable<FileClustersStatistics> ClusterizationCompleted { get; }

    IObservable<ScanProgress> ScanProgress { get; }

    bool IsScanning { get; }

    IObservable<int> TotalFilesCount { get; }

    int CurrentTotalFiles { get; }

    Task StartAsync(string directoryPath, bool useClusterization = false, CancellationToken cancellationToken = default);

    void Stop();
}
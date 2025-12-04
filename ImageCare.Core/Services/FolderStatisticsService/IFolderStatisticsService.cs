using ImageCare.Core.Domain.Folders;

namespace ImageCare.Core.Services.FolderStatisticsService;

public interface IFolderStatisticsService : IDisposable
{
    IObservable<FilesBucket> BucketChanged { get; }

    IObservable<ScanProgress> ScanProgress { get; }

    bool IsScanning { get; }

    IObservable<int> TotalFilesCount { get; }

    int CurrentTotalFiles { get; }

    Task StartAsync(string directoryPath, CancellationToken cancellationToken = default);

    void Stop();
}
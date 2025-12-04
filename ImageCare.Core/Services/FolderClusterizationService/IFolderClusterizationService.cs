namespace ImageCare.Core.Services.FolderClusterizationService;

public interface IFolderClusterizationService : IDisposable
{
    IObservable<FileClustersStatistics> ClusterizationCompleted { get; }

    bool IsScanning { get; }

    Task StartAsync(string directoryPath, CancellationToken cancellationToken = default);

    void Stop();
}
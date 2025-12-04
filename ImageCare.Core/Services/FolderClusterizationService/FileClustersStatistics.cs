using ImageCare.Core.Services.FolderStatisticsService;

namespace ImageCare.Core.Services.FolderClusterizationService;

public sealed class FileClustersStatistics
{
    private readonly Dictionary<string, FilesCluster> _fileToClusterMap;
    private readonly List<FilesCluster> _clusters;

    public FileClustersStatistics()
    {
        _fileToClusterMap = new Dictionary<string, FilesCluster>();
    }

    public FileClustersStatistics(IEnumerable<FilesCluster> clusters)
        : this()
    {
        _clusters = new List<FilesCluster>(clusters);

        // Build lookup dictionary for fast file path searches
        foreach (var cluster in _clusters)
        {
            foreach (var file in cluster.Files.Values)
            {
                _fileToClusterMap[file.FullName] = cluster;
            }
        }
    }

    public IReadOnlyList<FilesCluster> Clusters => _clusters.AsReadOnly();

    public int TotalFiles => _clusters.Sum(c => c.FileCount);

    public int TotalClusters => _clusters.Count;

    public TimeSpan TotalDuration =>
        _clusters.Any()
            ? _clusters.Max(c => c.EndTime) - _clusters.Min(c => c.StartTime)
            : TimeSpan.Zero;

    public FilesCluster? GetClusterByFilePath(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            return null;
        }

        return _fileToClusterMap.GetValueOrDefault(filePath);
    }

    public void AddCluster(FilesCluster cluster)
    {
        if (cluster == null)
        {
            throw new ArgumentNullException(nameof(cluster));
        }

        if (_clusters.Any(c => c.Id == cluster.Id))
        {
            throw new InvalidOperationException($"Cluster with ID {cluster.Id} already exists");
        }

        _clusters.Add(cluster);

        // Update lookup dictionary
        foreach (var file in cluster.Files.Values)
        {
            _fileToClusterMap[file.FullName] = cluster;
        }
    }

    public bool ContainsFile(string filePath)
    {
        return _fileToClusterMap.ContainsKey(filePath);
    }
}
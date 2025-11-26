using ImageCare.Core.Domain.Folders;

namespace ImageCare.Core.Services.FolderStatisticsService;

public sealed class DbScanClusteringService
{
    private const int NoisePointMarker = -1;
    private const int UnvisitedPointMarker = 0;

    public FileClustersStatistics ClusterAutomatically(IEnumerable<ClusterFileModel> files, int minPoints = 2, double epsilonMinutes = 1.0)
    {
        if (!files.Any())
        {
            return new FileClustersStatistics();
        }

        // Take only valid files
        var validFiles = files
                         .Where(f => f.CreatedDateTime.HasValue)
                         .OrderBy(f => f.CreatedDateTime)
                         .ToList();

        if (!validFiles.Any())
        {
            return new FileClustersStatistics();
        }

        var points = validFiles.Select(f => f.Timestamp).ToArray();

        var labels = PerformDbscan(points, epsilonMinutes / (24.0 * 60.0), minPoints);

        var clusterGroups = validFiles
                            .Select((file, index) => new { File = file, ClusterId = labels[index] })
                            .Where(x => x.ClusterId != NoisePointMarker)
                            .GroupBy(x => x.ClusterId)
                            .OrderBy(g => g.Min(x => x.File.CreatedDateTime))
                            .ToList();

        // Clusters
        var clusters = new List<FilesCluster>();
        foreach (var group in clusterGroups)
        {
            var cluster = new FilesCluster(clusters.Count + 1, group.Select(x => x.File).OrderBy(f => f.CreatedDateTime).ToList());
            clusters.Add(cluster);
        }

        // Single points
        var noiseFiles = validFiles
                         .Where((file, index) => labels[index] == NoisePointMarker)
                         .OrderBy(f => f.CreatedDateTime)
                         .ToList();

        // Create clusters for single files
        foreach (var noiseFile in noiseFiles)
        {
            var singleCluster = new FilesCluster(clusters.Count + 1, Enumerable.Repeat(noiseFile, 1));
            clusters.Add(singleCluster);
        }

        var colors = GenerateDistinctColorStrings(clusters.Count);
        foreach (var filesCluster in clusters)
        {
            filesCluster.ColorCode = colors.Dequeue();
        }

        return new FileClustersStatistics(clusters.OrderByDescending(c => c.StartTime));
    }

    private int[] PerformDbscan(double[] points, double epsilon, int minPoints)
    {
        var n = points.Length;
        var labels = new int[n];
        var clusterId = 0;

        for (var i = 0; i < n; i++)
        {
            if (labels[i] != UnvisitedPointMarker)
            {
                continue;
            }

            var neighbors = FindNeighbors(points, i, epsilon);

            if (neighbors.Count < minPoints)
            {
                labels[i] = NoisePointMarker;
                continue;
            }

            clusterId++;
            labels[i] = clusterId;

            var seedSet = new Queue<int>(neighbors);

            while (seedSet.Count > 0)
            {
                var j = seedSet.Dequeue();

                if (labels[j] == NoisePointMarker)
                {
                    labels[j] = clusterId;
                }

                if (labels[j] != UnvisitedPointMarker)
                {
                    continue;
                }

                labels[j] = clusterId;

                var newNeighbors = FindNeighbors(points, j, epsilon);
                if (newNeighbors.Count >= minPoints)
                {
                    foreach (var neighbor in newNeighbors)
                    {
                        seedSet.Enqueue(neighbor);
                    }
                }
            }
        }

        return labels;
    }

    private List<int> FindNeighbors(double[] points, int index, double epsilon)
    {
        var neighbors = new List<int>();
        var currentPoint = points[index];

        for (var i = 0; i < points.Length; i++)
        {
            if (i == index)
            {
                continue;
            }

            var distance = Math.Abs(points[i] - currentPoint);

            if (distance == 0)
            {
                neighbors.Add(i);
            }

            if (distance <= epsilon)
            {
                neighbors.Add(i);
            }
        }

        return neighbors;
    }

    private static Queue<string> GenerateDistinctColorStrings(int count)
    {
        // Уже перемешанные цвета для хорошего чередования
        var shuffledColors = new[]
        {
            "#FF0000", "#00FF00", "#0000FF", "#FF00FF", "#FFFF00", "#00FFFF", "#FF8000", "#8000FF", "#00FF80",
            "#FF0080", "#0080FF", "#80FF00", "#FF4000", "#4000FF", "#00FF40", "#FF0040", "#0040FF", "#40FF00",
            "#FFC000", "#C000FF", "#00FFA0", "#FF00C0", "#00A0FF", "#C0FF00", "#FF6000", "#6000FF", "#00FF60",
            "#FF0060", "#0060FF", "#60FF00", "#FFA000", "#A000FF", "#00FFA0", "#FF00A0", "#00A0FF", "#A0FF00",
            "#FF6666", "#6666FF", "#66FF66", "#FF66FF", "#FFFF66", "#66FFFF", "#FF9966", "#9966FF", "#66FF99",
            "#FF66CC", "#66CCFF", "#CCFF66", "#FFB366", "#B366FF", "#66FFB3", "#FF66B3", "#66B3FF", "#B3FF66",
            "#FFCC66", "#CC66FF", "#66FFCC", "#FF66FF", "#66CCFF", "#66FFCC", "#CCFF66", "#FF66CC", "#FFCC99",
            "#CC99FF", "#99FFCC", "#FF99CC", "#99CCFF", "#CCFF99", "#E6194B", "#3CB44B", "#4363D8", "#FFE119",
            "#F58230", "#911EB4", "#46F0F0", "#F032E6", "#BCF60C", "#FABEBE", "#008080", "#E6BEFF", "#9A6324",
            "#FFFAC8", "#800000", "#AAFFC3", "#808000", "#FFD8B1", "#000075", "#808080", "#000000", "#FF6B6B",
            "#4ECDC4", "#45B7D1", "#96CEB4", "#FFEEAD", "#D4A5A5", "#A2D2FF", "#FFAFCC", "#BDE0FE", "#FFC8DD",
            "#CDB4DB", "#FFC6FF", "#BDB2FF", "#A0C4FF", "#9BF6FF", "#CAFFBF", "#FDFFB6", "#FFD6A5", "#FFADAD",
            "#C7F9CC", "#A0E7E5", "#B5DEFF", "#F5CAC3", "#84A59D", "#F28482", "#84DCC6", "#A5A5A5", "#F7EDF0",
            "#F4F1DE", "#E07A5F", "#3D405B", "#81B29A", "#F2CC8F", "#E9C46A", "#F4A261", "#2A9D8F", "#264653",
            "#E76F51", "#F4F1BB", "#9BC1BC", "#5D576B", "#ED6A5A", "#F4F1DE", "#E6EBE0", "#9BC1BC", "#5CA4A9",
            "#ED6A5A", "#F4F1DE", "#36C5F0", "#2EB67D", "#ECB22E", "#E01E5A", "#4A154B", "#FF6B35", "#F7C59F",
            "#EFEFD0", "#004E89", "#1A659E", "#FF6B6B", "#4ECDC4", "#45B7D1", "#96CEB4", "#FFEEAD", "#D4A5A5",
            "#A2D2FF", "#FFAFCC", "#BDE0FE", "#FF5C8D", "#5C6BFF", "#5CFF8D", "#FF8D5C", "#8D5CFF", "#5CFFD6",
            "#FFD65C", "#D65CFF", "#5C8DFF", "#8DFF5C", "#FF5CD6", "#D6FF5C", "#5CD6FF", "#FFAA5C", "#5CFFAA",
            "#AA5CFF", "#5CAAFF", "#AAFF5C", "#FF5CAA", "#5CFF73", "#735CFF", "#FF735C", "#5C73FF", "#73FF5C",
            "#FF5C73", "#5CFFC1", "#C15CFF", "#FFC15C", "#5CC1FF", "#C1FF5C", "#FF5CC1", "#5CFFE3", "#E35CFF",
            "#FFE35C", "#5CE3FF", "#E3FF5C", "#FF5CE3", "#5CFF9B", "#9B5CFF", "#FF9B5C", "#5C9BFF", "#9BFF5C",
            "#FF5C9B", "#5CFFB7", "#B75CFF", "#FFB75C", "#5CB7FF", "#B7FF5C", "#FF5CB7", "#5CFF8F", "#8F5CFF",
            "#FF8F5C", "#5C8FFF", "#8FFF5C", "#FF5C8F", "#5CFFD1", "#D15CFF", "#FFD15C", "#5CD1FF", "#D1FF5C",
            "#FF5CD1", "#5CFFA5", "#A55CFF", "#FFA55C", "#5CA5FF", "#A5FF5C", "#FF5CA5", "#5CFFBD", "#BD5CFF",
            "#FFBD5C", "#5CBDFF", "#BDFF5C", "#FF5CBD", "#5CFF67", "#675CFF", "#FF675C", "#5C67FF", "#67FF5C",
            "#FF5C67", "#5CFFDB", "#DB5CFF", "#FFDB5C", "#5CDBFF", "#DBFF5C", "#FF5CDB", "#5CFF7F", "#7F5CFF",
            "#FF7F5C", "#5C7FFF", "#7FFF5C", "#FF5C7F", "#5CFFE9", "#E95CFF", "#FFE95C", "#5CE9FF", "#E9FF5C",
            "#FF5CE9", "#5CFF93", "#935CFF", "#FF935C", "#5C93FF", "#93FF5C", "#FF5C93", "#5CFFC7", "#C75CFF",
            "#FFC75C", "#5CC7FF", "#C7FF5C", "#FF5CC7", "#5CFFAB", "#AB5CFF", "#FFAB5C", "#5CABFF", "#ABFF5C",
            "#FF5CAB", "#5CFFDF", "#DF5CFF", "#FFDF5C", "#5CDFFF", "#DFFF5C", "#FF5CDF", "#5CFF87", "#875CFF",
            "#FF875C", "#5C87FF", "#87FF5C", "#FF5C87", "#5CFFF1", "#F15CFF", "#FFF15C", "#5CF1FF", "#F1FF5C",
            "#FF5CF1", "#5CFF9F", "#9F5CFF", "#FF9F5C", "#5C9FFF", "#9FFF5C", "#FF5C9F", "#5CFFCB", "#CB5CFF",
            "#FFCB5C", "#5CCBFF", "#CBFF5C", "#FF5CCB", "#5CFFB3", "#B35CFF", "#FFB35C", "#5CB3FF", "#B3FF5C",
            "#FF5CB3"
        };

        var colors = new Queue<string>();

        for (var i = 0; i < count; i++)
        {
            var colorIndex = i % shuffledColors.Length;
            colors.Enqueue(shuffledColors[colorIndex]);
        }

        return colors;
    }
}
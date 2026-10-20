using System.Collections.Concurrent;
using System.Text;

using ImageCare.Core.Domain.Folders;
using ImageCare.Core.Exceptions;
using ImageCare.Core.Services.ConfigurationService;

namespace ImageCare.Core.Services.FolderHistoryService;

public sealed class LocalFolderHistoryService : IFolderHistoryService
{
    private const int DefaultBufferSize = 200;
    private const int DefaultMinIntersections = 3;

    private readonly IConfigurationService _configurationService;
    private readonly ConcurrentQueue<string> _recentFolders = new();
    private readonly List<string> _popularFolders = [];
    private readonly ConcurrentDictionary<string, IReadOnlyList<SmartDirectoryModel>> _smartFoldersCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _syncRoot = new();
    private readonly int _bufferSize = DefaultBufferSize;

    public LocalFolderHistoryService(IConfigurationService configurationService)
    {
        _configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
        LoadHistory();
    }

    public event EventHandler? HistoryChanged;

    public void AddFolderToHistory(string folderPath)
    {
        if (string.Equals(folderPath, DirectoryModel.RootPath, StringComparison.Ordinal) ||
            !TryNormalizeExistingPath(folderPath, out var normalizedPath))
        {
            return;
        }

        var changed = false;
        lock (_syncRoot)
        {
            var comparer = GetPathComparer();
            var currentPaths = _recentFolders.ToList();
            var updatedPaths = currentPaths
                              .Where(path => !comparer.Equals(path, normalizedPath))
                              .Prepend(normalizedPath)
                              .Take(_bufferSize)
                              .ToList();

            changed = !currentPaths.SequenceEqual(updatedPaths, comparer);
            if (changed)
            {
                ReplaceRecentFolders(updatedPaths);
                _smartFoldersCache.Clear();
            }
        }

        if (changed)
        {
            HistoryChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void AddFolderToPopular(string folderPath)
    {
        if (!TryNormalizeExistingPath(folderPath, out var normalizedPath))
        {
            return;
        }

        var changed = false;
        lock (_syncRoot)
        {
            var comparer = GetPathComparer();
            if (!_popularFolders.Contains(normalizedPath, comparer))
            {
                _popularFolders.Insert(0, normalizedPath);
                changed = true;
            }
        }

        if (changed)
        {
            HistoryChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void RemoveFolderFromPopular(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            return;
        }

        string normalizedPath;
        try
        {
            normalizedPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folderPath));
        }
        catch (Exception)
        {
            return;
        }

        var changed = false;
        lock (_syncRoot)
        {
            changed = _popularFolders.RemoveAll(path => GetPathComparer().Equals(path, normalizedPath)) > 0;
        }

        if (changed)
        {
            HistoryChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public IReadOnlyList<string> GetPopularFolders()
    {
        lock (_syncRoot)
        {
            return _popularFolders.ToArray();
        }
    }

    public IReadOnlyList<string> GetRecentFolders()
    {
        lock (_syncRoot)
        {
            return _recentFolders.ToArray();
        }
    }

    public IReadOnlyList<SmartDirectoryModel> GetSmartFolders(int minIntersections = DefaultMinIntersections)
    {
        if (minIntersections < 1)
        {
            throw new ArgumentException("Minimum intersections must be at least 1", nameof(minIntersections));
        }

        var recentFolders = GetRecentFolders();
        if (recentFolders.Count < 2)
        {
            return Array.Empty<SmartDirectoryModel>();
        }

        var cacheKey = $"{recentFolders.Count}_{minIntersections}";

        // Try to get from cache
        if (_smartFoldersCache.TryGetValue(cacheKey, out var cachedFolders))
        {
            return cachedFolders;
        }

        var smartFolders = ComputeSmartFolders(recentFolders, minIntersections);

        // Cache the result
        lock (_syncRoot)
        {
            _smartFoldersCache[cacheKey] = smartFolders;
        }

        return smartFolders;
    }

    public void SaveHistory()
    {
        try
        {
            var configuration = _configurationService.Configuration.Value;

            lock (_syncRoot)
            {
                configuration.RecentFolderPaths = _recentFolders.ToList();
                configuration.PopularFolderPaths = _popularFolders.ToList();

                // Save current smart folders for persistence (using default min intersections)
                var smartFolders = ComputeSmartFolders(_recentFolders.ToArray(), DefaultMinIntersections);
                configuration.SmartFolderPaths = smartFolders.Select(f => f.Path).ToList();
            }

            _configurationService.SaveConfiguration();
        }
        catch (Exception ex)
        {
            throw new ServiceException("Failed to save folder history", ex);
        }
    }

    public void LoadHistory()
    {
        try
        {
            var configuration = _configurationService.Configuration.Value;

            lock (_syncRoot)
            {
                _recentFolders.Clear();
                foreach (var path in configuration.RecentFolderPaths.Take(_bufferSize))
                {
                    if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
                    {
                        _recentFolders.Enqueue(path);
                    }
                }

                _popularFolders.Clear();
                foreach (var path in configuration.PopularFolderPaths)
                {
                    if (TryNormalizeExistingPath(path, out var normalizedPath) &&
                        !_popularFolders.Contains(normalizedPath, GetPathComparer()))
                    {
                        _popularFolders.Add(normalizedPath);
                    }
                }

                _smartFoldersCache.Clear();
            }
        }
        catch (Exception ex)
        {
            throw new ServiceException("Failed to load folder history", ex);
        }
    }

    private void ReplaceRecentFolders(IEnumerable<string> paths)
    {
        _recentFolders.Clear();
        foreach (var path in paths)
        {
            _recentFolders.Enqueue(path);
        }
    }

    private static bool TryNormalizeExistingPath(string folderPath, out string normalizedPath)
    {
        normalizedPath = string.Empty;
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            return false;
        }

        try
        {
            normalizedPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folderPath));
            return Directory.Exists(normalizedPath);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static StringComparer GetPathComparer()
    {
        return OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    }

    private IReadOnlyList<SmartDirectoryModel> ComputeSmartFolders(IReadOnlyList<string> recentFolders, int minIntersections)
    {
        var pathIntersections = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var pathBuilder = new StringBuilder(260);

        // Find all common paths at all levels
        for (var i = 0; i < recentFolders.Count - 1; i++)
        {
            for (var j = i + 1; j < recentFolders.Count; j++)
            {
                GetAllCommonPaths(recentFolders[i], recentFolders[j], pathBuilder, pathIntersections);
            }
        }

        // Filter by minimum intersections and create sorted result
        return pathIntersections
               .Where(kvp => kvp.Value >= minIntersections)
               .Select(kvp => new
               {
                   Path = kvp.Key,
                   MatchCount = kvp.Value,
                   Depth = kvp.Key.Count(c => c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar)
               })
               .OrderBy(p => p.Depth)
               .ThenByDescending(p => p.MatchCount)
               .ThenBy(p => p.Path)
               .Select(p => new SmartDirectoryModel(
                           Path.GetFileName(p.Path) ?? p.Path,
                           p.Path,
                           p.MatchCount))
               .ToArray();
    }

    private void GetAllCommonPaths(string path1, string path2, StringBuilder pathBuilder, Dictionary<string, int> intersections)
    {
        pathBuilder.Clear();

        if (string.IsNullOrWhiteSpace(path1) || string.IsNullOrWhiteSpace(path2))
        {
            return;
        }

        var normalizedPath1 = Path.GetFullPath(path1);
        var normalizedPath2 = Path.GetFullPath(path2);
        var root1 = Path.GetPathRoot(normalizedPath1) ?? string.Empty;
        var root2 = Path.GetPathRoot(normalizedPath2) ?? string.Empty;

        if (!string.Equals(root1, root2, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var separators = new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };
        var dirs1 = normalizedPath1[root1.Length..].Split(separators, StringSplitOptions.RemoveEmptyEntries);
        var dirs2 = normalizedPath2[root2.Length..].Split(separators, StringSplitOptions.RemoveEmptyEntries);

        pathBuilder.Append(root1);

        var minLength = Math.Min(dirs1.Length, dirs2.Length);

        for (var i = 0; i < minLength; i++)
        {
            if (!string.Equals(dirs1[i], dirs2[i], StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            if (pathBuilder.Length > 0 &&
                pathBuilder[pathBuilder.Length - 1] != Path.DirectorySeparatorChar &&
                pathBuilder[pathBuilder.Length - 1] != Path.AltDirectorySeparatorChar)
            {
                pathBuilder.Append(Path.DirectorySeparatorChar);
            }

            pathBuilder.Append(dirs1[i]);

            var currentPath = pathBuilder.ToString();
            var rootPath = root1;

            // Skip root paths like "C:\" or empty paths
            if (!string.IsNullOrEmpty(currentPath) && currentPath != rootPath)
            {
                intersections.TryGetValue(currentPath, out var count);
                intersections[currentPath] = count + 1;
            }
        }
    }

    private IReadOnlyList<SmartDirectoryModel> GetSmartFoldersFromConfiguration(int minIntersections)
    {
        var configuration = _configurationService.Configuration.Value;
        var recentFolders = GetRecentFolders();

        if (recentFolders.Count < 2)
        {
            return Array.Empty<SmartDirectoryModel>();
        }

        var result = new List<SmartDirectoryModel>();
        var pathBuilder = new StringBuilder(260);

        foreach (var path in configuration.SmartFolderPaths)
        {
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            {
                // Calculate match count against current recent folders
                var matchCount = 0;

                for (var i = 0; i < recentFolders.Count - 1; i++)
                {
                    for (var j = i + 1; j < recentFolders.Count; j++)
                    {
                        var commonPath = FindCommonPath(recentFolders[i], recentFolders[j], pathBuilder);
                        if (string.Equals(commonPath, path, StringComparison.OrdinalIgnoreCase))
                        {
                            matchCount++;
                        }
                    }
                }

                if (matchCount >= minIntersections)
                {
                    result.Add(new SmartDirectoryModel(Path.GetFileName(path), path, matchCount));
                }
            }
        }

        // Sort by depth, then match count, then path
        return result
               .OrderBy(f => f.Path.Count(c => c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar))
               .ThenByDescending(f => f.MatchCount)
               .ThenBy(f => f.Path)
               .ToArray();
    }

    private string FindCommonPath(string path1, string path2, StringBuilder pathBuilder)
    {
        pathBuilder.Clear();

        var dirs1 = path1.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var dirs2 = path2.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        var minLength = Math.Min(dirs1.Length, dirs2.Length);

        for (var i = 0; i < minLength; i++)
        {
            if (!string.Equals(dirs1[i], dirs2[i], StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            if (pathBuilder.Length > 0)
            {
                pathBuilder.Append(Path.DirectorySeparatorChar);
            }

            pathBuilder.Append(dirs1[i]);
        }

        return pathBuilder.ToString();
    }
}
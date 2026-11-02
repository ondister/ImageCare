using System;
using System.Collections.Generic;
using System.Linq;

using ImageCare.Core.Domain.Folders;
using ImageCare.UI.Avalonia.Collections;
using ImageCare.UI.Avalonia.ViewModels.Preview;

namespace ImageCare.UI.Avalonia.ViewModels;

/// <summary>
/// Maintains the ordered, path-unique file set for one folder surface.
/// </summary>
internal sealed class MediaFolderFileIndex
{
    private readonly object _sync = new();
    private readonly IEqualityComparer<string> _pathComparer;
    private SortedObservableCollection<FileModel> _files;
    private HashSet<string> _paths;

    public MediaFolderFileIndex(
        IComparer<FileModel> comparer,
        IEqualityComparer<string>? pathComparer = null)
    {
        ArgumentNullException.ThrowIfNull(comparer);

        _pathComparer = pathComparer ?? PreviewPathUtility.PathComparer;
        _files = new SortedObservableCollection<FileModel>(comparer);
        _paths = new HashSet<string>(_pathComparer);
    }

    public void Reset(IComparer<FileModel> comparer)
    {
        ArgumentNullException.ThrowIfNull(comparer);

        lock (_sync)
        {
            _files = new SortedObservableCollection<FileModel>(comparer);
            _paths = new HashSet<string>(_pathComparer);
        }
    }

    public FileModel[] Snapshot()
    {
        lock (_sync)
        {
            return _files.ToArray();
        }
    }

    public bool AddIfMissing(FileModel file)
    {
        ArgumentNullException.ThrowIfNull(file);

        lock (_sync)
        {
            if (!_paths.Add(file.FullName))
            {
                return false;
            }

            _files.Add(file);
            return true;
        }
    }

    public FileModel[] AddIfMissing(IEnumerable<FileModel> files)
    {
        ArgumentNullException.ThrowIfNull(files);

        var candidates = files.ToArray();
        foreach (var file in candidates)
        {
            ArgumentNullException.ThrowIfNull(file);
        }

        lock (_sync)
        {
            var updatedPaths = new HashSet<string>(_paths, _pathComparer);
            var added = new List<FileModel>();
            foreach (var file in candidates)
            {
                if (updatedPaths.Add(file.FullName))
                {
                    added.Add(file);
                }
            }

            _files.AddRange(added);
            _paths = updatedPaths;
            return added.ToArray();
        }
    }

    public bool Remove(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        lock (_sync)
        {
            if (!_paths.Remove(path))
            {
                return false;
            }

            _files.RemoveWhere(file => _pathComparer.Equals(file.FullName, path));
            return true;
        }
    }

    public void ChangeComparer(IComparer<FileModel> comparer)
    {
        ArgumentNullException.ThrowIfNull(comparer);

        lock (_sync)
        {
            _files.SetComparer(comparer);
        }
    }

    public MediaFolderFileDifference FindStaleAndMissing(IEnumerable<FileModel> actualFiles)
    {
        ArgumentNullException.ThrowIfNull(actualFiles);

        var actualByPath = new Dictionary<string, FileModel>(_pathComparer);
        foreach (var file in actualFiles)
        {
            ArgumentNullException.ThrowIfNull(file);
            actualByPath.TryAdd(file.FullName, file);
        }

        lock (_sync)
        {
            var stalePaths = _files
                .Where(file => !actualByPath.ContainsKey(file.FullName))
                .Select(file => file.FullName)
                .ToArray();
            var missingFiles = actualByPath
                .Where(pair => !_paths.Contains(pair.Key))
                .Select(pair => pair.Value)
                .ToArray();

            return new MediaFolderFileDifference(stalePaths, missingFiles);
        }
    }
}

internal readonly record struct MediaFolderFileDifference(
    string[] StalePaths,
    FileModel[] MissingFiles);

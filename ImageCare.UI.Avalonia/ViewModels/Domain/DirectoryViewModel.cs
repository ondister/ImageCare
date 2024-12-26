using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

using AutoMapper;

using ImageCare.Core.Domain;
using ImageCare.Core.Services.FolderService;
using ImageCare.Mvvm;
using ImageCare.Mvvm.Collections;

using Serilog;

namespace ImageCare.UI.Avalonia.ViewModels.Domain;

[DebuggerDisplay("{Path}")]
internal class DirectoryViewModel : ViewModelBase, IComparable<DirectoryViewModel>
{
    private readonly IFolderService _folderService;
    private readonly ILogger _logger;
    private readonly IMapper _mapper;
    private bool _isExpanded;
    private bool _isLoaded;
    private string? _name;
    private string _path;

    public DirectoryViewModel(string? name,
                              string path,
                              IEnumerable<DirectoryViewModel> children,
                              IFolderService folderService,
                              IMapper mapper,
                              ILogger logger)
    {
        _folderService = folderService;
        _logger = logger;
        _mapper = mapper;
        Name = name;
        Path = path;

        ChildFileSystemItems = new SortedObservableCollection<DirectoryViewModel>(children);
        ChildFileSystemItems.CollectionChanged += OnChildFileSystemItemsCollectionChanged;
    }

    public string? Name
    {
        get => _name;
       private set => SetProperty(ref _name, value);
    }

    public string Path
    {
        get => _path;
        private set => SetProperty(ref _path, value);
    }

    public FileManagerPanel FileManagerPanel { get; private set; }

    public SortedObservableCollection<DirectoryViewModel> ChildFileSystemItems { get; }

    public bool IsLoaded
    {
        get => _isLoaded;
        set => SetProperty(ref _isLoaded, value);
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (SetProperty(ref _isExpanded, value) && _isExpanded)
            {
                if (this is DeviceViewModel)
                {
                    return;
                }

                ChildFileSystemItems.Clear();
                _ = SeedFileSystemItemsAsync();
            }

            if (_isExpanded)
            {
                _folderService.AddVisitingFolder(_mapper.Map<DirectoryModel>(this), FileManagerPanel);
            }
            else
            {
                _folderService.RemoveVisitingFolder(_mapper.Map<DirectoryModel>(this), FileManagerPanel);
            }
        }
    }

    /// <inheritdoc />
    public int CompareTo(DirectoryViewModel? other)
    {
        if (ReferenceEquals(this, other))
        {
            return 0;
        }

        if (ReferenceEquals(null, other))
        {
            return 1;
        }

        return string.Compare(Path, other.Path, StringComparison.Ordinal);
    }

    public DirectoryViewModel? FindChildByPathRecursively(string pathToFind)
    {
        return FindPathRecursive(ChildFileSystemItems, pathToFind);
    }

    public void SetFileManagerPanel(FileManagerPanel panel)
    {
        FileManagerPanel = panel;

        foreach (var child in ChildFileSystemItems)
        {
            child.FileManagerPanel = FileManagerPanel;
        }
    }

    private DirectoryViewModel? FindPathRecursive(SortedObservableCollection<DirectoryViewModel> directories, string pathToFind)
    {
        foreach (var directory in directories)
        {
            if (directory.Path.Equals(pathToFind, StringComparison.OrdinalIgnoreCase))
            {
                return directory;
            }

            if (directory.ChildFileSystemItems.Count == 0)
            {
                continue;
            }

            if (FindPathRecursive(directory.ChildFileSystemItems, pathToFind) is { } foundDirectoryViewModel)
            {
                return foundDirectoryViewModel;
            }
        }

        return null;
    }

    private void OnChildFileSystemItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs eventArgs)
    {
        if (eventArgs.NewItems == null)
        {
            return;
        }

        foreach (var item in eventArgs.NewItems.OfType<DirectoryViewModel>())
        {
            item.FileManagerPanel = FileManagerPanel;
        }
    }

    private async Task SeedFileSystemItemsAsync()
    {
        IsLoaded = true;
        try
        {
            var currentDirectoryModel = await _folderService.GetDirectoryModelAsync(Path);

            foreach (var directoryModel in currentDirectoryModel.DirectoryModels)
            {
                var fileSystemItemViewModel = _mapper.Map<DirectoryViewModel>(directoryModel);

                ChildFileSystemItems.InsertItem(fileSystemItemViewModel);
            }
        }
        catch (Exception exception)
        {
            _logger.Error(exception, $"Unexpected exception during getting files from folder: {Path}");
        }
        finally
        {
            IsLoaded = false;
        }
    }

    public void UpdateDirectory(DirectoryModel newDirectoryModel)
    {
        Name = newDirectoryModel.Name;
        Path = newDirectoryModel.Path;
    }
}
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;

using AutoMapper;

using ExCSS;

using ImageCare.Core.Domain.Folders;
using ImageCare.Core.Services.FileSystemService;
using ImageCare.Core.Services.FolderService;
using ImageCare.Mvvm;
using ImageCare.Mvvm.Collections;

using Serilog;

namespace ImageCare.UI.Avalonia.ViewModels.Domain;

[DebuggerDisplay("{Path}")]
internal class DirectoryViewModel : ViewModelBase, IComparable<DirectoryViewModel>
{
    private readonly IFolderService _folderService;
    private readonly IFileSystemService _fileSystemService;
    private readonly ILogger _logger;
    private readonly IMapper _mapper;
    private bool _isExpanded;
    private bool _isLoaded;
    private string? _name;
    private string _path;
    private bool _hasSupportedMedia;
    private bool _isEditing;
    private string? _editableName;

    public DirectoryViewModel(string? name,
                              string path,
                              IEnumerable<DirectoryViewModel> children,
                              IFolderService folderService,
                              IFileSystemService fileSystemService,
                              IMapper mapper,
                              ILogger logger)
    {
        _folderService = folderService ?? throw new ArgumentNullException(nameof(folderService));
        _fileSystemService = fileSystemService ?? throw new ArgumentNullException(nameof(fileSystemService));
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        ChildFileSystemItems = new SortedObservableCollection<DirectoryViewModel>(children);
        ChildFileSystemItems.CollectionChanged += OnChildFileSystemItemsCollectionChanged;

        Name = name;
        Path = path;

        RenameFolderCommand = CreateCommand(RenameFolder, () => IsEditing);
        StartRenameFolderCommand = CreateCommand(StartRenameFolder);
        NameTextBoxLostFocusCommand = CreateCommand(NameTextBoxLostFocus);
    }

    public ICommand StartRenameFolderCommand { get; }

    public ICommand RenameFolderCommand { get; }

    public ICommand NameTextBoxLostFocusCommand { get; }

    public string? Name
    {
        get => _name;
        private set
        {
            SetProperty(ref _name, value);
            EditableName = value;
        }
    }

    public string? EditableName
    {
        get => _editableName;
        set => SetProperty(ref _editableName, value);
    }

    public string Path
    {
        get => _path;
        private set => SetProperty(ref _path, value);
    }

    public bool IsEditing
    {
        get => _isEditing;
        set => SetProperty(ref _isEditing, value);
    }

    public FileManagerPanel FileManagerPanel { get; private set; }

    public SortedObservableCollection<DirectoryViewModel> ChildFileSystemItems { get; }

    public bool IsLoaded
    {
        get => _isLoaded;
        private set => SetProperty(ref _isLoaded, value);
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (SetProperty(ref _isExpanded, value))
            {
                HandleExpansionChange();
            }
        }
    }

    public bool HasSupportedMedia
    {
        get => _hasSupportedMedia;
        set => SetProperty(ref _hasSupportedMedia, value);
    }

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
        try
        {
            return FindPathRecursive(ChildFileSystemItems, pathToFind);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to find child by path: {Path}", pathToFind);

            return null;
        }
    }

    public void SetFileManagerPanel(FileManagerPanel panel)
    {
        FileManagerPanel = panel;
        foreach (var child in ChildFileSystemItems)
        {
            child.FileManagerPanel = FileManagerPanel;
        }
    }

    public void UpdateDirectory(DirectoryModel newDirectoryModel)
    {
        try
        {
            Name = newDirectoryModel.Name;
            Path = newDirectoryModel.Path;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to update directory: {Path}", newDirectoryModel.Path);
        }
    }

    public async Task GotoDirectoryAsync(DirectoryViewModel targetDirectory)
    {
        if (targetDirectory == null)
        {
            throw new ArgumentNullException(nameof(targetDirectory));
        }

        try
        {
            // Check if we're already at the target directory
            if (Path.Equals(targetDirectory.Path, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // Check if target is a descendant of current directory
            if (!targetDirectory.Path.StartsWith(Path, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Target directory is not a descendant of current directory");
            }

            await NavigateToPathAsync(targetDirectory.Path);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to navigate to directory: {TargetPath}", targetDirectory.Path);
        }
    }

    public async Task GotoDirectoryFromRootAsync(DirectoryViewModel targetDirectory)
    {
        if (targetDirectory == null)
        {
            throw new ArgumentNullException(nameof(targetDirectory));
        }

        try
        {
            // Find the appropriate drive that contains the target path
            var targetDrive = ChildFileSystemItems.OfType<DriveViewModel>()
                                                  .FirstOrDefault(drive => targetDirectory.Path.StartsWith(drive.Path, StringComparison.OrdinalIgnoreCase));

            if (targetDrive == null)
            {
                throw new InvalidOperationException($"No drive contains the target path: {targetDirectory.Path}");
            }

            if (!targetDrive.IsExpanded)
            {
                targetDrive.IsExpanded = true;
            }

            // Navigate from the drive to the target
            await targetDrive.GotoDirectoryAsync(targetDirectory);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to navigate from root to directory: {TargetPath}", targetDirectory.Path);
        }
    }

    private void RenameFolder()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(EditableName))
            {
                EditableName = Name;
                IsEditing = false;
                return;
            }

            var newName = _fileSystemService.RenameFolder(EditableName, Path);
            if (!string.IsNullOrEmpty(newName))
            {
                Name = newName;
            }

            IsEditing = false;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to rename folder from {OldName} to {NewName}", Name, EditableName);
            EditableName = Name; // Restore on error
        }
    }

    private void StartRenameFolder()
    {
        try
        {
            IsEditing = true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to start folder rename");
        }
    }

    private void NameTextBoxLostFocus()
    {
        try
        {
            RenameFolder();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to handle name text box lost focus");
        }
    }

    private void HandleExpansionChange()
    {
        try
        {
            if (_isExpanded)
            {
                if (this is not DeviceViewModel and not SpecialDirectoryViewModel)
                {
                    ChildFileSystemItems.Clear();

                    _ = SeedFileSystemItemsAsync(); // Fire and forget. We should get subdirs anyway
                }

                _folderService.AddVisitingFolder(_mapper.Map<DirectoryModel>(this), FileManagerPanel);
            }
            else
            {
                _folderService.RemoveVisitingFolder(_mapper.Map<DirectoryModel>(this), FileManagerPanel);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, $"Failed to handle expansion change for: {Path}");
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

            if (pathToFind.StartsWith(directory.Path, StringComparison.OrdinalIgnoreCase) && directory.ChildFileSystemItems.Count > 0)
            {
                if (FindPathRecursive(directory.ChildFileSystemItems, pathToFind) is { } foundDirectoryViewModel)
                {
                    return foundDirectoryViewModel;
                }
            }
        }

        return null;
    }

    private void OnChildFileSystemItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs eventArgs)
    {
        try
        {
            if (eventArgs.NewItems != null)
            {
                foreach (var item in eventArgs.NewItems.OfType<DirectoryViewModel>())
                {
                    item.FileManagerPanel = FileManagerPanel;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to handle child collection change");
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

                ChildFileSystemItems.Add(fileSystemItemViewModel);
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

    private async Task NavigateToPathAsync(string targetPath)
    {
        var currentPath = Path;

        // Get relative path segments
        var relativePath = targetPath.Substring(currentPath.Length).TrimStart(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);

        if (string.IsNullOrEmpty(relativePath))
        {
            return;
        }

        var pathSegments = relativePath.Split(
            [System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);

        var currentDirectory = this;

        foreach (var segment in pathSegments)
        {
            try
            {
                // Expand the current directory to show its children
                if (!currentDirectory.IsExpanded)
                {
                    currentDirectory.IsExpanded = true;
                    await Task.Delay(3000);
                }


                var childDirectory = currentDirectory.ChildFileSystemItems
                                                     .FirstOrDefault(c => string.Equals(c.Name, segment, StringComparison.OrdinalIgnoreCase));

                // Move to the next level
                currentDirectory = childDirectory;
            }
            catch (Exception ex)
            {
                _logger.Error(
                    ex,
                    "Failed to navigate to segment {Segment} in path {TargetPath}",
                    segment,
                    targetPath);
            }
        }

        if (!currentDirectory.IsExpanded)
        {
            currentDirectory.IsExpanded = true;
        }
    }
}
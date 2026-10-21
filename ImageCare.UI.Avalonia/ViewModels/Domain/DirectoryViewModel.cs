using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

using ImageCare.Core.Domain.Folders;
using ImageCare.Core.Services.FileSystemService;
using ImageCare.Core.Services.FolderService;

using MapsterMapper;

using Serilog;

namespace ImageCare.UI.Avalonia.ViewModels.Domain;

public enum DirectoryLoadState
{
    Initial,
    Loading,
    Loaded 
}

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
    private string _path = string.Empty;
    private bool _hasSupportedMedia;
    private bool _isEditing;
    private string? _editableName;
    private CancellationTokenSource? _expantionCancellationTokenSource;
    private DirectoryLoadState _loadState;
    private readonly SemaphoreSlim _loadingSemaphore = new SemaphoreSlim(1, 1);
    private bool _isSelected;
    private bool _isRenaming;
    private DirectoryViewModel? _parent;
    private int? _recentOrder;
    private bool _isPinnedPopular;

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

        ChildFileSystemItems = new Collections.SortedObservableCollection<DirectoryViewModel>(children);
        ChildFileSystemItems.CollectionChanged += OnChildFileSystemItemsCollectionChanged;
        foreach (var child in ChildFileSystemItems)
        {
            child._parent = this;
        }

        Name = name;
        Path = path;

        RenameFolderCommand = CreateCommand(RenameFolder, () => IsEditing);
        StartRenameFolderCommand = CreateCommand(StartRenameFolder);
        NameTextBoxLostFocusCommand = CreateCommand(NameTextBoxLostFocus);
        DeleteFolderCommand = CreateCommand(DeleteFolder, CanDeleteFolder);
        CreateFolderCommand = CreateCommand(CreateFolder, CanCreateFolder);
    }

    public ICommand StartRenameFolderCommand { get; }

    public ICommand RenameFolderCommand { get; }

    public ICommand CreateFolderCommand { get; }

    public ICommand DeleteFolderCommand { get; }

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

    public DirectoryViewModel? Parent => _parent;

    public Collections.SortedObservableCollection<DirectoryViewModel> ChildFileSystemItems { get; }

    public bool IsLoaded
    {
        get => _isLoaded;
        private set => SetProperty(ref _isLoaded, value);
    }
    public DirectoryLoadState LoadState
    {
        get => _loadState;
        private set
        {
            if (SetProperty(ref _loadState, value))
            {
                RaisePropertyChanged(nameof(IsLoading));
                RaiseFolderIconPropertiesChanged();
            }
        }
    }

    public bool IsLoading => LoadState == DirectoryLoadState.Loading;

    public bool IsOpen => IsSelected || (IsExpanded && ChildFileSystemItems.Count > 0);

    public bool IsOpenFolderIconVisible => !IsLoading && IsOpen;

    public bool IsClosedRegularFolder => !IsLoading && !IsOpen && !HasSupportedMedia;

    public bool IsClosedMediaFolder => !IsLoading && !IsOpen && HasSupportedMedia;

    public bool IsRecentEntry => _recentOrder.HasValue;

    public bool IsPinnedPopular => _isPinnedPopular;

    public int RecentOrder => _recentOrder ?? int.MaxValue;

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (SetProperty(ref _isExpanded, value))
            {
                RaiseFolderIconPropertiesChanged();
                HandleExpansionChange();
            }
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value))
            {
                RaiseFolderIconPropertiesChanged();
            }
        }
    }

    public bool HasSupportedMedia
    {
        get => _hasSupportedMedia;
        set
        {
            if (SetProperty(ref _hasSupportedMedia, value))
            {
                RaiseFolderIconPropertiesChanged();
            }
        }
    }

    public void SetRecentOrder(int? order, bool isPinnedPopular = false)
    {
        if (_recentOrder == order && _isPinnedPopular == isPinnedPopular)
        {
            return;
        }

        _recentOrder = order;
        _isPinnedPopular = isPinnedPopular;
        RaisePropertyChanged(nameof(IsRecentEntry));
        RaisePropertyChanged(nameof(IsPinnedPopular));
        RaisePropertyChanged(nameof(RecentOrder));
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

        if (IsRecentEntry && other.IsRecentEntry)
        {
            return RecentOrder.CompareTo(other.RecentOrder);
        }

        return string.Compare(Path, other.Path, StringComparison.Ordinal);
    }

    private void RaiseFolderIconPropertiesChanged()
    {
        RaisePropertyChanged(nameof(IsOpen));
        RaisePropertyChanged(nameof(IsOpenFolderIconVisible));
        RaisePropertyChanged(nameof(IsClosedRegularFolder));
        RaisePropertyChanged(nameof(IsClosedMediaFolder));
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
            child.SetFileManagerPanel(panel);
        }
    }

    public void UpdateDirectory(DirectoryModel newDirectoryModel)
    {
        try
        {
            var oldPath = Path;
            Name = newDirectoryModel.Name;
            UpdatePathRecursively(oldPath, newDirectoryModel.Path);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to update directory: {Path}", newDirectoryModel.Path);
        }
    }

    private void UpdatePathRecursively(string oldBasePath, string newBasePath)
    {
        Path = newBasePath;

        foreach (var child in ChildFileSystemItems)
        {
            var relativePath = System.IO.Path.GetRelativePath(oldBasePath, child.Path);
            child.UpdatePathRecursively(child.Path, System.IO.Path.Combine(newBasePath, relativePath));
        }
    }

    private void RenameFolder()
    {
        if (!IsEditing || _isRenaming)
        {
            return;
        }

        _isRenaming = true;
        try
        {
            var requestedName = EditableName?.Trim();
            if (string.IsNullOrWhiteSpace(requestedName))
            {
                EditableName = Name;
                return;
            }

            if (string.Equals(requestedName, Name, StringComparison.Ordinal))
            {
                return;
            }

            var oldDirectory = _mapper.Map<DirectoryModel>(this);
            var newName = _fileSystemService.RenameFolder(requestedName, oldDirectory.Path);
            if (string.IsNullOrEmpty(newName) || string.Equals(newName, Name, StringComparison.Ordinal))
            {
                EditableName = Name;
                return;
            }

            var parentPath = System.IO.Path.GetDirectoryName(oldDirectory.Path)
                             ?? throw new InvalidOperationException($"Cannot rename root directory '{oldDirectory.Path}'");
            var renamedDirectory = new DirectoryModel(newName, System.IO.Path.Combine(parentPath, newName));

            var expandedDirectories = EnumerateSubtree()
                                     .Where(directory => directory.IsExpanded)
                                     .Select(directory => _mapper.Map<DirectoryModel>(directory))
                                     .ToArray();
            foreach (var expandedDirectory in expandedDirectories)
            {
                _folderService.RemoveVisitingFolder(expandedDirectory, FileManagerPanel);
            }

            UpdateDirectory(renamedDirectory);
            _parent?.ChildFileSystemItems.RefreshSort();

            foreach (var expandedDirectory in EnumerateSubtree().Where(directory => directory.IsExpanded))
            {
                _folderService.AddVisitingFolder(
                    _mapper.Map<DirectoryModel>(expandedDirectory),
                    FileManagerPanel);
            }

            if (IsSelected)
            {
                _folderService.SetSelectedDirectory(new SelectedDirectory(renamedDirectory, FileManagerPanel));
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to rename folder from {OldName} to {NewName}", Name, EditableName);
            EditableName = Name;
        }
        finally
        {
            IsEditing = false;
            _isRenaming = false;
        }
    }

    private IEnumerable<DirectoryViewModel> EnumerateSubtree()
    {
        var pending = new Stack<DirectoryViewModel>();
        pending.Push(this);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            yield return current;

            foreach (var child in current.ChildFileSystemItems)
            {
                pending.Push(child);
            }
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
            if (IsEditing && !_isRenaming)
            {
                RenameFolder();
            }
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
                _expantionCancellationTokenSource?.Cancel();
                _expantionCancellationTokenSource?.Dispose();
                _expantionCancellationTokenSource = new CancellationTokenSource();

                if (this is not DeviceViewModel and not SpecialDirectoryViewModel)
                {
                    if (LoadState == DirectoryLoadState.Initial)
                    {
                        ChildFileSystemItems.Clear();
                    }

                    _ = LoadExpandedDirectoryAsync(_expantionCancellationTokenSource.Token);
                }

                _folderService.AddVisitingFolder(_mapper.Map<DirectoryModel>(this), FileManagerPanel);
            }
            else
            {
                _expantionCancellationTokenSource?.Cancel();
                if (this is not DeviceViewModel and not SpecialDirectoryViewModel)
                {
                    LoadState = DirectoryLoadState.Initial;
                }

                _folderService.RemoveVisitingFolder(_mapper.Map<DirectoryModel>(this), FileManagerPanel);
            }
        }
        catch (OperationCanceledException)
        {

            LoadState = DirectoryLoadState.Initial;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, $"Failed to handle expansion change for: {Path}");
            LoadState = DirectoryLoadState.Initial;
        }
    }

    private DirectoryViewModel? FindPathRecursive(Collections.SortedObservableCollection<DirectoryViewModel> directories, string pathToFind)
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
            RaiseFolderIconPropertiesChanged();

            if (eventArgs.NewItems != null)
            {
                foreach (var item in eventArgs.NewItems.OfType<DirectoryViewModel>())
                {
                    item._parent = this;
                    item.SetFileManagerPanel(FileManagerPanel);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to handle child collection change");
        }
    }

    private async Task LoadExpandedDirectoryAsync(CancellationToken cancellationToken)
    {
        try
        {
            await SeedFileSystemItemsAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Collapsing or re-expanding a node cancels the previous load.
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load expanded directory: {Path}", Path);
        }
    }

    private async Task SeedFileSystemItemsAsync(CancellationToken cancellationToken)
    {
        await _loadingSemaphore.WaitAsync(cancellationToken);

        try
        {
            if (LoadState == DirectoryLoadState.Loaded)
            {
                return;
            }

            LoadState = DirectoryLoadState.Loading;
            cancellationToken.ThrowIfCancellationRequested();

            var currentDirectoryModel = await _folderService.GetDirectoryModelAsync(Path);

            if (ChildFileSystemItems.Count == 0 ||
                ChildFileSystemItems.All(c => c.LoadState != DirectoryLoadState.Loaded))
            {
                ChildFileSystemItems.Clear();

                foreach (var directoryModel in currentDirectoryModel.DirectoryModels)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ChildFileSystemItems.Add(_mapper.Map<DirectoryViewModel>(directoryModel));
                }
            }

            LoadState = DirectoryLoadState.Loaded;
        }
        catch
        {
            LoadState = DirectoryLoadState.Initial;
            throw;
        }
        finally
        {
            _loadingSemaphore.Release();
        }
    }

    public void InvalidateChildren()
    {
        if (this is not DeviceViewModel and not SpecialDirectoryViewModel)
        {
            LoadState = DirectoryLoadState.Initial;
        }
    }

    public async Task EnsureLoadedAsync(CancellationToken cancellationToken = default)
    {
        if (LoadState == DirectoryLoadState.Loaded)
        {
            return;
        }

        if (!IsExpanded)
        {
            IsExpanded = true;
        }

        // Await the real load instead of relying on the fire-and-forget expansion handler.
        // The semaphore coalesces this call with a load that expansion already started.
        await SeedFileSystemItemsAsync(cancellationToken);
    }

    private void CreateFolder()
    {
        try
        {
            var createdSubFolder = _folderService.CreateSubFolder(_mapper.Map<DirectoryModel>(this));
            if (createdSubFolder == null)
            {
                return;
            }

            var createdViewModel = ChildFileSystemItems.FirstOrDefault(child =>
                child.Path.Equals(createdSubFolder.Path, StringComparison.OrdinalIgnoreCase));
            if (createdViewModel == null)
            {
                createdViewModel = _mapper.Map<DirectoryViewModel>(createdSubFolder);
                ChildFileSystemItems.Add(createdViewModel);
            }

            createdViewModel.IsEditing = true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to create folder for: {SelectedPath}", Path);
        }
    }

    private void DeleteFolder()
    {
        try
        {
            if (HasSupportedMedia)
            {
                return;
            }

            var deletedDirectory = _mapper.Map<DirectoryModel>(this);
            _folderService.RemoveFolder(deletedDirectory);
            if (_fileSystemService.DirectoryExists(deletedDirectory.Path))
            {
                return;
            }

            if (IsExpanded)
            {
                _folderService.RemoveVisitingFolder(deletedDirectory, FileManagerPanel);
            }

            if (IsSelected)
            {
                _folderService.SetSelectedDirectory(new SelectedDirectory(DirectoryModel.Empty, FileManagerPanel));
            }

            _parent?.ChildFileSystemItems.Remove(this);
            _parent = null;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to delete folder: {Path}", Path);
        }
    }

    private bool CanCreateFolder()
    {

        return !IsEditing;
    }

    private bool CanDeleteFolder()
    {
        return !HasSupportedMedia;
    }
}
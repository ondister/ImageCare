using System;
using System.IO;
using System.Linq;
using System.Threading;

using ImageCare.Core.Domain.Folders;
using ImageCare.Core.Services.FolderHistoryService;
using ImageCare.UI.Avalonia.Localization;
using ImageCare.UI.Avalonia.Services;
using ImageCare.UI.Avalonia.ViewModels.Domain;

using MapsterMapper;

using Prism.Commands.Ex;

using Serilog;

namespace ImageCare.UI.Avalonia.ViewModels.Folders;

internal sealed class RecentFoldersViewModel : ViewModelBase
{
    private const int AutomaticFoldersDisplayLimit = 10;

    private readonly IFolderHistoryService _folderHistoryService;
    private readonly IFileDialogService _fileDialogService;
    private readonly IMapper _mapper;
    private readonly LocalizationManager _localization;
    private readonly ILogger _logger;
    private readonly SynchronizationContext _synchronizationContext;
    private readonly Func<DeviceViewModel?> _getRoot;
    private readonly Func<FileManagerPanel> _getFileManagerPanel;
    private readonly Action<DirectoryViewModel> _clearSelectionIfCurrent;
    private readonly StringComparer _pathComparer;
    private bool _isStarted;

    public RecentFoldersViewModel(
        IFolderHistoryService folderHistoryService,
        IFileDialogService fileDialogService,
        IMapper mapper,
        LocalizationManager localization,
        ILogger logger,
        SynchronizationContext synchronizationContext,
        Func<DeviceViewModel?> getRoot,
        Func<FileManagerPanel> getFileManagerPanel,
        Action<DirectoryViewModel> clearSelectionIfCurrent,
        StringComparer? pathComparer = null)
    {
        _folderHistoryService = folderHistoryService ?? throw new ArgumentNullException(nameof(folderHistoryService));
        _fileDialogService = fileDialogService ?? throw new ArgumentNullException(nameof(fileDialogService));
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        _localization = localization ?? throw new ArgumentNullException(nameof(localization));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _synchronizationContext = synchronizationContext ?? throw new ArgumentNullException(nameof(synchronizationContext));
        _getRoot = getRoot ?? throw new ArgumentNullException(nameof(getRoot));
        _getFileManagerPanel = getFileManagerPanel ?? throw new ArgumentNullException(nameof(getFileManagerPanel));
        _clearSelectionIfCurrent = clearSelectionIfCurrent ?? throw new ArgumentNullException(nameof(clearSelectionIfCurrent));
        _pathComparer = pathComparer ?? GetPlatformPathComparer();

        AddRecentFolderCommand = CreateAsyncCommand(AddRecentFolderAsync);
        AddFolderToRecentCommand = CreateCommand<DirectoryViewModel>(AddFolderToRecent);
        RemoveRecentFolderCommand = CreateCommand<DirectoryViewModel>(RemoveRecentFolder);
    }

    public AsyncDelegateCommand AddRecentFolderCommand { get; }

    public DelegateCommand<DirectoryViewModel> AddFolderToRecentCommand { get; }

    public DelegateCommand<DirectoryViewModel> RemoveRecentFolderCommand { get; }

    public void Start()
    {
        if (!_isStarted)
        {
            _folderHistoryService.HistoryChanged += OnFolderHistoryChanged;
            _isStarted = true;
        }

        Refresh();
    }

    public void Stop()
    {
        if (!_isStarted)
        {
            return;
        }

        _folderHistoryService.HistoryChanged -= OnFolderHistoryChanged;
        _isStarted = false;
    }

    public void Refresh()
    {
        try
        {
            var recentFolder = _getRoot()
                ?.ChildFileSystemItems
                 .OfType<SpecialDirectoryViewModel>()
                 .FirstOrDefault();
            if (recentFolder == null)
            {
                return;
            }

            var pinnedPaths = (_folderHistoryService.GetPopularFolders() ?? [])
                             .Where(path => !string.IsNullOrWhiteSpace(path))
                             .Distinct(_pathComparer)
                             .ToArray();
            var automaticPaths = (_folderHistoryService.GetSmartFolders(10) ?? [])
                                .Select(folder => folder.Path)
                                .Where(path => !string.IsNullOrWhiteSpace(path))
                                .Distinct(_pathComparer)
                                .Take(AutomaticFoldersDisplayLimit);
            var recentPaths = pinnedPaths
                             .Concat(automaticPaths)
                             .Distinct(_pathComparer)
                             .ToArray();

            var staleItems = recentFolder.ChildFileSystemItems
                                         .Where(item => !recentPaths.Any(path => PathsEqual(path, item.Path)))
                                         .ToArray();
            foreach (var staleItem in staleItems)
            {
                recentFolder.ChildFileSystemItems.Remove(staleItem);
            }

            for (var index = 0; index < recentPaths.Length; index++)
            {
                var path = recentPaths[index];
                var item = recentFolder.ChildFileSystemItems
                                       .FirstOrDefault(existing => PathsEqual(existing.Path, path));
                var isPinned = pinnedPaths.Any(pinnedPath => PathsEqual(pinnedPath, path));
                if (item == null)
                {
                    var name = Path.GetFileName(path);
                    var model = new DirectoryModel(string.IsNullOrWhiteSpace(name) ? path : name, path);
                    item = _mapper.Map<DirectoryViewModel>(model);
                    item.SetFileManagerPanel(_getFileManagerPanel());
                    item.SetRecentOrder(index, isPinned);
                    recentFolder.ChildFileSystemItems.Add(item);
                }
                else
                {
                    item.SetRecentOrder(index, isPinned);
                }

                var currentIndex = recentFolder.ChildFileSystemItems.IndexOf(item);
                if (currentIndex != index)
                {
                    recentFolder.ChildFileSystemItems.Move(currentIndex, index);
                }
            }

            recentFolder.IsExpanded = true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to refresh Recent folders");
        }
    }

    private async System.Threading.Tasks.Task AddRecentFolderAsync()
    {
        try
        {
            var folderPath = await _fileDialogService.ShowOpenFolderDialogAsync(
                _localization["Folders_SelectRecent"]);
            if (string.IsNullOrWhiteSpace(folderPath))
            {
                return;
            }

            _folderHistoryService.AddFolderToPopular(folderPath);
            _folderHistoryService.SaveHistory();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to add a folder to Recent");
        }
    }

    private void AddFolderToRecent(DirectoryViewModel directory)
    {
        if (directory == null || directory.IsRecentEntry ||
            directory is DeviceViewModel or SpecialDirectoryViewModel)
        {
            return;
        }

        try
        {
            _folderHistoryService.AddFolderToPopular(directory.Path);
            _folderHistoryService.SaveHistory();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to add a folder to Recent: {Path}", directory.Path);
        }
    }

    private void RemoveRecentFolder(DirectoryViewModel directory)
    {
        if (directory == null || !directory.IsPinnedPopular)
        {
            return;
        }

        try
        {
            _clearSelectionIfCurrent(directory);
            _folderHistoryService.RemoveFolderFromPopular(directory.Path);
            _folderHistoryService.SaveHistory();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to remove a folder from Recent: {Path}", directory.Path);
        }
    }

    private void OnFolderHistoryChanged(object? sender, EventArgs e)
    {
        _synchronizationContext.Post(_ => Refresh(), null);
    }

    private bool PathsEqual(string firstPath, string secondPath)
    {
        return _pathComparer.Equals(NormalizePath(firstPath), NormalizePath(secondPath));
    }

    private static string NormalizePath(string path)
    {
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }

    private static StringComparer GetPlatformPathComparer()
    {
        return OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    }
}

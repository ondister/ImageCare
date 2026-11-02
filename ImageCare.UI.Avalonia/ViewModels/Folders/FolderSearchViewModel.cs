using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using ImageCare.Core.Domain.Folders;
using ImageCare.Core.Services.FileSystemService;
using ImageCare.UI.Avalonia.Localization;
using ImageCare.UI.Avalonia.ViewModels.Domain;

using MapsterMapper;

using Prism.Commands.Ex;

namespace ImageCare.UI.Avalonia.ViewModels.Folders;

internal sealed class FolderSearchViewModel : ViewModelBase
{
    private readonly IFileSystemService _fileSystemService;
    private readonly IMapper _mapper;
    private readonly LocalizationManager _localization;
    private readonly SynchronizationContext _synchronizationContext;
    private readonly Func<DirectoryViewModel, Task> _openSearchResultAsync;

    private CancellationTokenSource? _searchCancellationTokenSource;
    private DirectoryViewModel? _selectedFolder;
    private string? _searchText;
    private bool _isHostLoading;
    private bool _isSearching;
    private string? _searchHeader;
    private bool _isSearchEnabled;
    private bool _isInSearchSession;
    private string _searchTarget;

    public FolderSearchViewModel(
        IFileSystemService fileSystemService,
        IMapper mapper,
        LocalizationManager localization,
        SynchronizationContext synchronizationContext,
        Func<DirectoryViewModel, Task> openSearchResultAsync)
    {
        _fileSystemService = fileSystemService ?? throw new ArgumentNullException(nameof(fileSystemService));
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        _localization = localization ?? throw new ArgumentNullException(nameof(localization));
        _synchronizationContext = synchronizationContext ?? throw new ArgumentNullException(nameof(synchronizationContext));
        _openSearchResultAsync = openSearchResultAsync ?? throw new ArgumentNullException(nameof(openSearchResultAsync));
        _searchTarget = _localization["Folders_SearchFolders"];

        PerformSearchCommand = CreateAsyncCommand(PerformSearchAsync, CanPerformSearch)
            .ObservesProperty(() => SearchText)
            .ObservesProperty(() => IsSearching)
            .ObservesProperty(() => IsHostLoading);
        ClearSearchCommand = CreateCommand(Clear, () => IsInSearchSession)
            .ObservesProperty(() => IsInSearchSession);
        SetSearchResultCommand = CreateAsyncCommand<DirectoryViewModel>(_openSearchResultAsync);

        SearchResults = new ObservableCollection<DirectoryViewModel>();
    }

    public ObservableCollection<DirectoryViewModel> SearchResults { get; }

    public AsyncDelegateCommand PerformSearchCommand { get; }

    public DelegateCommand ClearSearchCommand { get; }

    public AsyncDelegateCommand<DirectoryViewModel> SetSearchResultCommand { get; }

    public string SearchTarget
    {
        get => _searchTarget;
        private set => SetProperty(ref _searchTarget, value);
    }

    public bool IsSearchEnabled
    {
        get => _isSearchEnabled;
        private set => SetProperty(ref _isSearchEnabled, value);
    }

    public bool IsSearching
    {
        get => _isSearching;
        private set => SetProperty(ref _isSearching, value);
    }

    public bool IsInSearchSession
    {
        get => _isInSearchSession;
        private set => SetProperty(ref _isInSearchSession, value);
    }

    public string? SearchText
    {
        get => _searchText;
        set => SetProperty(ref _searchText, value);
    }

    public string? SearchHeader
    {
        get => _searchHeader;
        private set => SetProperty(ref _searchHeader, value);
    }

    public bool HasSearchText => !string.IsNullOrWhiteSpace(SearchText) && SearchText.Length >= 3;

    private bool IsHostLoading
    {
        get => _isHostLoading;
        set => SetProperty(ref _isHostLoading, value);
    }

    public void SetHostLoading(bool isLoading)
    {
        IsHostLoading = isLoading;
    }

    public void SetSelectedFolder(DirectoryViewModel? folder)
    {
        _selectedFolder = folder;
        if (folder == null)
        {
            return;
        }

        IsSearchEnabled = folder is not (SpecialDirectoryViewModel or DriveViewModel or DeviceViewModel);
        SearchTarget = _localization.Get("Folders_SearchFoldersIn", folder.Name);
    }

    public void OnCultureChanged()
    {
        SearchTarget = _selectedFolder == null
            ? _localization["Folders_SearchFolders"]
            : _localization.Get("Folders_SearchFoldersIn", _selectedFolder.Name);

        if (IsSearching)
        {
            SearchHeader = _localization.Get(
                "Folders_SearchHeader",
                SearchText,
                _selectedFolder?.Name ?? _localization["Folders_AllFolders"]);
        }
    }

    public void Clear()
    {
        CancelSearch();
        SearchText = null;
        SearchResults.Clear();
        IsSearching = false;
        IsInSearchSession = false;
    }

    private async Task PerformSearchAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchText) || _selectedFolder == null)
        {
            return;
        }

        CancelSearch();

        var cancellation = new CancellationTokenSource();
        _searchCancellationTokenSource = cancellation;
        var cancellationToken = cancellation.Token;

        try
        {
            SearchResults.Clear();
            IsSearching = true;
            IsInSearchSession = true;

            SearchHeader = _localization.Get(
                "Folders_SearchHeader",
                SearchText,
                _selectedFolder.Name ?? _localization["Folders_AllFolders"]);

            var searchText = SearchText;
            var searchRootPath = _selectedFolder.Path;

            await Task.Run(
                () =>
                {
                    var directories = _fileSystemService
                        .EnumerateDirectories(searchRootPath, $"*{searchText}*", SearchOption.AllDirectories)
                        .Select(path => new DirectoryModel(Path.GetFileName(path), path));
                    foreach (var directory in directories)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        _synchronizationContext.Send(
                            _ => SearchResults.Add(_mapper.Map<DirectoryViewModel>(directory)),
                            null);
                    }
                },
                cancellationToken);

            SearchHeader = _localization.Get("Folders_SearchFound", SearchResults.Count, SearchText);
        }
        catch (OperationCanceledException)
        {
            SearchHeader = _localization["Folders_SearchCancelled"];
        }
        catch (Exception)
        {
            SearchHeader = _localization["Folders_SearchFailed"];
        }
        finally
        {
            if (ReferenceEquals(_searchCancellationTokenSource, cancellation))
            {
                _searchCancellationTokenSource = null;
                IsSearching = false;
                if (cancellationToken.IsCancellationRequested)
                {
                    ResetSession();
                }
            }

            cancellation.Dispose();
        }
    }

    private void CancelSearch()
    {
        var cancellation = _searchCancellationTokenSource;
        _searchCancellationTokenSource = null;
        cancellation?.Cancel();
    }

    private void ResetSession()
    {
        SearchText = null;
        SearchResults.Clear();
        IsSearching = false;
        IsInSearchSession = false;
    }

    private bool CanPerformSearch()
    {
        return !IsHostLoading && HasSearchText && !IsSearching;
    }
}

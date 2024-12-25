using System;
using System.Threading.Tasks;
using System.Windows.Input;

using AutoMapper;

using CommunityToolkit.Mvvm.Input;

using ImageCare.Core.Domain;
using ImageCare.Core.Services;
using ImageCare.Mvvm;
using ImageCare.Mvvm.Collections;
using ImageCare.UI.Avalonia.ViewModels.Domain;

using Prism.Regions;

using Serilog;

namespace ImageCare.UI.Avalonia.ViewModels;

internal class FoldersViewModel : ViewModelBase
{
    private readonly IFolderService _folderService;
    private readonly IMapper _mapper;
    private readonly ILogger _logger;
    private DirectoryViewModel? _selectedFileSystemItem;

    public FoldersViewModel(IFolderService folderService,
                            IMapper mapper,
                            ILogger logger)
    {
        _folderService = folderService;
        _mapper = mapper;
        _logger = logger;
        OnViewLoadedCommand = new AsyncRelayCommand(OnViewLoaded);

        FileSystemItemViewModels = new SortedObservableCollection<DirectoryViewModel>();
    }

    public SortedObservableCollection<DirectoryViewModel> FileSystemItemViewModels { get; }

    public ICommand OnViewLoadedCommand { get; }

    public DirectoryViewModel? SelectedFileSystemItem
    {
        get => _selectedFileSystemItem;
        set
        {
            if (SetProperty(ref _selectedFileSystemItem, value) && _selectedFileSystemItem != null)
            {
                _folderService.SetSelectedDirectory(new SelectedDirectory(_selectedFileSystemItem.Name, _selectedFileSystemItem.Path, FileManagerPanel));
            }
        }
    }

    public FileManagerPanel FileManagerPanel { get; private set; }

    /// <inheritdoc />
    public override void OnNavigatedTo(NavigationContext navigationContext)
    {
        FileManagerPanel = (FileManagerPanel)navigationContext.Parameters["panel"];
    }

    private async Task OnViewLoaded()
    {
        try
        {
            var root = await _folderService.GetDirectoryModelAsync();

            var rootViewModel = _mapper.Map<DirectoryViewModel>(root);
            rootViewModel.IsExpanded = true;

            FileSystemItemViewModels.InsertItem(rootViewModel);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Unexpected exception during root folders loading");
        }
    }
}
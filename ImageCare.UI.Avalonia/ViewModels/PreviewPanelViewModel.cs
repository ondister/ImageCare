using System;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;

using AutoMapper;

using ImageCare.Core.Domain;
using ImageCare.Core.Services;
using ImageCare.Core.Services.FileOperationsService;
using ImageCare.Mvvm;
using ImageCare.Mvvm.Collections;
using ImageCare.UI.Avalonia.Behaviors;
using ImageCare.UI.Avalonia.ViewModels.Domain;

using Prism.Regions;

using Serilog;

namespace ImageCare.UI.Avalonia.ViewModels;

internal class PreviewPanelViewModel : ViewModelBase
{
    private readonly IFileSystemImageService _imageService;
    private readonly IFolderService _folderService;
    private readonly IFileSystemWatcherService _fileSystemWatcherService;
    private readonly IFileOperationsService _fileOperationsService;
    private readonly IMapper _mapper;
    private readonly ILogger _logger;
    private readonly SynchronizationContext _synchronizationContext;
    private ImagePreviewViewModel? _selectedPreview;
    private CompositeDisposable _fileSystemWatcherCompositeDisposable;

    public PreviewPanelViewModel(IFileSystemImageService imageService,
                                 IFolderService folderService,
                                 IFileSystemWatcherService fileSystemWatcherService,
                                 IFileOperationsService fileOperationsService,
                                 IMapper mapper,
                                 ILogger logger,
                                 ImagePreviewDropHandler imagePreviewDropHandler,
                                 SynchronizationContext synchronizationContext)
    {
        _imageService = imageService;
        _folderService = folderService;
        _fileSystemWatcherService = fileSystemWatcherService;
        _fileOperationsService = fileOperationsService;
        _mapper = mapper;
        _logger = logger;
        _synchronizationContext = synchronizationContext;

        ImagePreviews = [];
        ImagePreviewDropHandler = imagePreviewDropHandler;
    }

    public SortedObservableCollection<ImagePreviewViewModel> ImagePreviews { get; }

    public ImagePreviewDropHandler ImagePreviewDropHandler { get; }

    public FileManagerPanel FileManagerPanel { get; private set; }

    public ImagePreviewViewModel? SelectedPreview
    {
        get => _selectedPreview;
        set
        {
            if (_selectedPreview != null)
            {
                _selectedPreview.Selected = false;
            }

            SetProperty(ref _selectedPreview, value);
            if (_selectedPreview != null)
            {
                _selectedPreview.Selected = true;

                _fileOperationsService.SetSelectedPreview(new SelectedImagePreview(_mapper.Map<ImagePreview>(_selectedPreview), FileManagerPanel));
            }
        }
    }

    public string SelectedFolderPath { get; set; }

    /// <inheritdoc />
    public override void OnNavigatedTo(NavigationContext navigationContext)
    {
        FileManagerPanel = (FileManagerPanel)navigationContext.Parameters["panel"];

        _fileSystemWatcherCompositeDisposable = new CompositeDisposable
        {
            _fileSystemWatcherService.FileCreated.DistinctUntilChanged(model => model.FullName).Subscribe(OnFileCreated),
            _fileSystemWatcherService.FileDeleted.DistinctUntilChanged(model => model.FullName).Subscribe(OnFileDeleted),
            _fileSystemWatcherService.FileRenamed.DistinctUntilChanged(model => model.NewFileModel.FullName).Subscribe(OnFileRenamed),
            _folderService.FileSystemItemSelected.Subscribe(OnFolderSelected),
            _fileOperationsService.ImagePreviewSelected.Subscribe(OnImagePreviewSelected)
        };
    }

    /// <inheritdoc />
    public override void OnNavigatedFrom(NavigationContext navigationContext)
    {
        _fileSystemWatcherCompositeDisposable.Dispose();
    }

    private async Task LoadImagePreviewsAsync(DirectoryModel directoryModel)
    {
        try
        {
            ImagePreviews.Clear();

            var files = await _folderService.GetFileModelAsync(directoryModel, "*");

            await foreach (var previewImage in _imageService.GetImagePreviewsAsync(files))
            {
                ImagePreviews.Add(_mapper.Map<ImagePreviewViewModel>(previewImage));
            }
        }
        catch (Exception exception)
        {
            _logger.Error(exception, $"Unexpected exception during loading image previews from folder: {directoryModel.Path}");
        }
    }

    private void OnFileCreated(FileModel fileModel)
    {
        try
        {
            CreateImagePreviewFromPath(fileModel.FullName);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, $"Unexpected exception during handling of file creation: {fileModel.FullName}");
        }
    }

    private void OnFileDeleted(FileModel fileModel)
    {
        try
        {
            RemoveImagePreviewByPath(fileModel.FullName);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, $"Unexpected exception during handling of file deletion: {fileModel.FullName}");
        }
    }

    private void OnFileRenamed(FileRenamedModel model)
    {
        try
        {
            RemoveImagePreviewByPath(model.OldFileModel.FullName);

            CreateImagePreviewFromPath(model.NewFileModel.FullName);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, $"Unexpected exception during handling of file renaming: {model.OldFileModel.FullName}");
        }
    }

    private void CreateImagePreviewFromPath(string filePath)
    {
        _imageService.GetImagePreviewAsync(filePath)
                     .ContinueWith(
                         task =>
                         {
                             if (task.Result == null)
                             {
                                 return;
                             }

                             ImagePreviews.InsertItem(_mapper.Map<ImagePreviewViewModel>(task.Result));
                         });
    }

    private void RemoveImagePreviewByPath(string filePath)
    {
        var imagePreviewViewModel = ImagePreviews.FirstOrDefault(vm => vm.Url.Equals(filePath, StringComparison.OrdinalIgnoreCase));
        if (imagePreviewViewModel != null)
        {
            var indexToRemove = ImagePreviews.IndexOf(imagePreviewViewModel);
            ImagePreviews.Remove(imagePreviewViewModel);

            if (ImagePreviews.Count > indexToRemove)
            {
                _synchronizationContext.Post(d => { SelectedPreview = ImagePreviews[indexToRemove]; }, null);
            }

            if (ImagePreviews.Count == 0)
            {
                _synchronizationContext.Post(d => { SelectedPreview = null; }, null);
                _fileOperationsService.SetSelectedPreview(new SelectedImagePreview(ImagePreview.Empty.Title, ImagePreview.Empty.Url, ImagePreview.Empty.MediaFormat, FileManagerPanel));
            }
        }
    }

    private void OnFolderSelected(SelectedDirectory selectedFileSystemItem)
    {
        if (selectedFileSystemItem.FileManagerPanel == FileManagerPanel)
        {
            _ = LoadImagePreviewsAsync(selectedFileSystemItem);

            SelectedFolderPath = selectedFileSystemItem.Path;

            if (!string.IsNullOrWhiteSpace(SelectedFolderPath))
            {
                try
                {
                    _fileSystemWatcherService.SetWatchingDirectory(SelectedFolderPath);
                }

                catch (Exception exception)
                {
                    _logger.Error(exception, $"Unexpected exception during set watching directory {SelectedFolderPath}");
                }
            }
        }
    }

    private void OnImagePreviewSelected(SelectedImagePreview selectedImagePreview)
    {
        if (selectedImagePreview.FileManagerPanel != FileManagerPanel)
        {
            SelectedPreview = null;
        }
    }
}
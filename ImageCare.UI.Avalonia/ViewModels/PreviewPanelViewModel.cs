using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

using AutoMapper;

using CommunityToolkit.Mvvm.Input;

using DynamicData;
using ImageCare.Core.Domain.Folders;
using ImageCare.Core.Domain.Media;
using ImageCare.Core.Domain.Preview;
using ImageCare.Core.Services.FileOperationsService;
using ImageCare.Core.Services.FileSystemImageService;
using ImageCare.Core.Services.FileSystemWatcherService;
using ImageCare.Core.Services.FolderService;
using ImageCare.Core.Services.NotificationService;
using ImageCare.UI.Avalonia.Behaviors;
using ImageCare.UI.Avalonia.ViewModels.Domain;

using Prism.Regions;

using Serilog;

namespace ImageCare.UI.Avalonia.ViewModels;

internal class PreviewPanelViewModel : NavigatedViewModelBase, IDisposable
{
    private readonly IFileSystemImageService _imageService;
    private readonly IFolderService _folderService;
    private readonly IFileSystemWatcherService _fileSystemWatcherService;
    private readonly IFileOperationsService _fileOperationsService;
    private readonly INotificationService _notificationService;
    private readonly IMapper _mapper;
    private readonly ILogger _logger;
    private readonly SynchronizationContext _synchronizationContext;
    private readonly SourceList<MediaPreviewViewModel> _sourceList = new();
    private Subject<IComparer<MediaPreviewViewModel>> _sortChanged;
    private ReadOnlyObservableCollection<MediaPreviewViewModel> _imagePreviews;
    private MediaPreviewViewModel? _selectedPreview;
    private CompositeDisposable _fileSystemWatcherCompositeDisposable;

    private CancellationTokenSource _folderSelectedCancellationTokenSource;
    private bool _previewsLoading;
    private SortingBy _selectedSorting;

    public PreviewPanelViewModel(IFileSystemImageService imageService,
                                 IFolderService folderService,
                                 IFileSystemWatcherService fileSystemWatcherService,
                                 IFileOperationsService fileOperationsService,
                                 INotificationService notificationService,
                                 IMapper mapper,
                                 ILogger logger,
                                 ImagePreviewDropHandler imagePreviewDropHandler,
                                 SynchronizationContext synchronizationContext)
    {
        _imageService = imageService;
        _folderService = folderService;
        _fileSystemWatcherService = fileSystemWatcherService;
        _fileOperationsService = fileOperationsService;
        _notificationService = notificationService;
        _mapper = mapper;
        _logger = logger;
        _synchronizationContext = synchronizationContext;

        ImagePreviewDropHandler = imagePreviewDropHandler;
        CopyImagePreviewCommand = new AsyncRelayCommand(CopyImagePreviewAsync);
        MoveImagePreviewCommand = new AsyncRelayCommand(MoveImagePreviewAsync);
        DeleteImagePreviewCommand = new AsyncRelayCommand(DeleteImagePreview);

        SetPreviewsSorting();

        _folderSelectedCancellationTokenSource = new CancellationTokenSource();
    }

    public ReadOnlyObservableCollection<MediaPreviewViewModel> ImagePreviews => _imagePreviews;

    public ImagePreviewDropHandler ImagePreviewDropHandler { get; }

    public FileManagerPanel FileManagerPanel { get; private set; }

    public MediaPreviewViewModel? SelectedPreview
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

                _fileOperationsService.SetSelectedPreview(new SelectedMediaPreview(_mapper.Map<MediaPreview>(_selectedPreview), FileManagerPanel));
            }
        }
    }

    public string SelectedFolderPath { get; set; }

    public bool PreviewsLoading
    {
        get => _previewsLoading;
        set => SetProperty(ref _previewsLoading, value);
    }

    public IEnumerable<SortingBy> SortingByList { get; private set; }

    public SortingBy SelectedSorting
    {
        get => _selectedSorting;
        set
        {
            SetProperty(ref _selectedSorting, value);

            _sortChanged.OnNext(_selectedSorting.GetComparer());
        }
    }

    public ICommand CopyImagePreviewCommand { get; }

    public ICommand MoveImagePreviewCommand { get; }

    public ICommand DeleteImagePreviewCommand { get; }

    /// <inheritdoc />
    public void Dispose()
    {
        _fileSystemWatcherCompositeDisposable.Dispose();
        _sourceList.Dispose();
        _sortChanged.Dispose();
        _folderSelectedCancellationTokenSource.Dispose();
    }

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

    private async Task CopyImagePreviewAsync()
    {
        if (SelectedPreview == null)
        {
            return;
        }

        var targetDirectory = _folderService.GetSelectedDirectory(FileManagerPanel == FileManagerPanel.Left ? FileManagerPanel.Right : FileManagerPanel.Left);

        if (targetDirectory == null)
        {
            return;
        }

        var notificationTitle = $"{SelectedPreview.Url} => {targetDirectory.Path}";
        _notificationService.SendNotification(new Notification(notificationTitle, string.Empty));
        var progress = new Progress<OperationInfo>();

        progress.ProgressChanged += (o, info) => { _notificationService.SendNotification(new Notification(notificationTitle, info.Percentage.ToString("F1"))); };

        var result = await _fileOperationsService.CopyImagePreviewToDirectoryAsync(
                         _mapper.Map<MediaPreview>(SelectedPreview),
                         targetDirectory.Path,
                         progress);

        switch (result)
        {
            case OperationResult.Success:
                _notificationService.SendNotification(new SuccessNotification(notificationTitle, ""));
                break;
            case OperationResult.Failed:
                _notificationService.SendNotification(new ErrorNotification(notificationTitle, ""));
                break;
        }
    }

    private async Task MoveImagePreviewAsync()
    {
        if (SelectedPreview == null)
        {
            return;
        }

        var targetDirectory = _folderService.GetSelectedDirectory(FileManagerPanel == FileManagerPanel.Left ? FileManagerPanel.Right : FileManagerPanel.Left);

        if (targetDirectory == null)
        {
            return;
        }

        var notificationTitle = $"{SelectedPreview.Url} => {targetDirectory.Path}";
        _notificationService.SendNotification(new Notification(notificationTitle, string.Empty));
        var progress = new Progress<OperationInfo>();

        progress.ProgressChanged += (o, info) => { _notificationService.SendNotification(new Notification(notificationTitle, info.Percentage.ToString("F1"))); };

        var result = await _fileOperationsService.MoveImagePreviewToDirectoryAsync(
                         _mapper.Map<MediaPreview>(SelectedPreview),
                         targetDirectory.Path,
                         progress);

        switch (result)
        {
            case OperationResult.Success:
                _notificationService.SendNotification(new SuccessNotification(notificationTitle, ""));
                break;
            case OperationResult.Failed:
                _notificationService.SendNotification(new ErrorNotification(notificationTitle, ""));
                break;
        }
    }

    private async Task DeleteImagePreview()
    {
        if (SelectedPreview == null)
        {
            return;
        }

        await SelectedPreview.RemoveImagePreviewAsync();
    }

    private void SetPreviewsSorting()
    {
        SortingByList =
        [
            SortingBy.NameDescending,
            SortingBy.NameAscending,
            SortingBy.DateTimeDescending,
            SortingBy.DateTimeAscending
        ];

        _sortChanged = new Subject<IComparer<MediaPreviewViewModel>>();
        var sort = _sortChanged.AsObservable();

        _sourceList.Connect()
                   .Sort(sort)
                   .Bind(out _imagePreviews)
                   .Subscribe();
    }

    private async Task LoadImagePreviewsAsync(DirectoryModel directoryModel, CancellationToken cancellationToken = default)
    {
        PreviewsLoading = true;

        try
        {
            ClearPreviewPanel();

            if (directoryModel.Path == string.Empty)
            {
                return;
            }

            var files = await _folderService.GetFileModelAsync(directoryModel, "*");

            await foreach (var previewImage in _imageService.GetMediaPreviewsAsync(files, cancellationToken))
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                await AddImagePreviewAsync(previewImage);
            }

            SelectedSorting = SortingBy.DateTimeAscending;
        }
        catch (Exception exception)
        {
            _logger.Error(exception, $"Unexpected exception during loading image previews from folder: {directoryModel.Path}");
        }
        finally
        {
            PreviewsLoading = false;
        }
    }

    private async Task AddImagePreviewAsync(MediaPreview previewImage)
    {
        var mediaPreviewViewModel = _mapper.Map<MediaPreviewViewModel>(previewImage);

        var metadata = await _imageService.GetMediaMetadataAsync(previewImage);
        mediaPreviewViewModel.MetadataString = metadata.GetString();
        mediaPreviewViewModel.DateTimeString = metadata.CreationDateTime.ToString("dd.MM.yyyy HH:mm");
        mediaPreviewViewModel.Metadata = metadata;

        mediaPreviewViewModel.RotateAngle = mediaPreviewViewModel.Metadata.Orientation.ToRotationAngle();

        _sourceList.Add(mediaPreviewViewModel);
    }

    private void OnFileCreated(FileModel fileModel)
    {
        try
        {
            CreateImagePreviewFromPathAsync(fileModel.FullName);
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

            CreateImagePreviewFromPathAsync(model.NewFileModel.FullName);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, $"Unexpected exception during handling of file renaming: {model.OldFileModel.FullName}");
        }
    }

    private async Task CreateImagePreviewFromPathAsync(string filePath)
    {
        var imagePreview = await _imageService.GetMediaPreviewAsync(filePath);

        if (imagePreview == null)
        {
            return;
        }

        if (ImagePreviews.Any(p => p.Url.Equals(filePath, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        await AddImagePreviewAsync(imagePreview);
    }

    private void RemoveImagePreviewByPath(string filePath)
    {
        var imagePreviewViewModel = ImagePreviews.FirstOrDefault(vm => vm.Url.Equals(filePath, StringComparison.OrdinalIgnoreCase));
        if (imagePreviewViewModel != null)
        {
            var indexToRemove = ImagePreviews.IndexOf(imagePreviewViewModel);
            _sourceList.Remove(imagePreviewViewModel);

            if (ImagePreviews.Count > indexToRemove)
            {
                _synchronizationContext.Post(d => { SelectedPreview = ImagePreviews[indexToRemove]; }, null);
            }

            if (ImagePreviews.Count == 0)
            {
                _synchronizationContext.Post(d => { SelectedPreview = null; }, null);
                _fileOperationsService.SetSelectedPreview(new SelectedMediaPreview(MediaPreview.Empty, FileManagerPanel));
            }
        }
    }

    private void OnFolderSelected(SelectedDirectory selectedFileSystemItem)
    {
        if (selectedFileSystemItem.FileManagerPanel == FileManagerPanel)
        {
            _folderSelectedCancellationTokenSource.Cancel();

            _folderSelectedCancellationTokenSource.Dispose();
            _folderSelectedCancellationTokenSource = new CancellationTokenSource();

            _ = LoadImagePreviewsAsync(selectedFileSystemItem, _folderSelectedCancellationTokenSource.Token);

            SelectedFolderPath = selectedFileSystemItem.Path;

            if (!string.IsNullOrWhiteSpace(SelectedFolderPath))
            {
                try
                {
                    _fileSystemWatcherService.StartWatchingDirectory(SelectedFolderPath);
                }

                catch (Exception exception)
                {
                    _logger.Error(exception, $"Unexpected exception during set watching directory {SelectedFolderPath}");
                }
            }
        }
    }

    private void ClearPreviewPanel()
    {
        _sourceList.Clear();
        SelectedPreview = null;
    }

    private void OnImagePreviewSelected(SelectedMediaPreview selectedImagePreview)
    {
        if (selectedImagePreview.FileManagerPanel != FileManagerPanel)
        {
            SelectedPreview = null;
        }
    }
}
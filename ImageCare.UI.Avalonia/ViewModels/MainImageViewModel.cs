using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

using Avalonia.Media.Imaging;

using ImageCare.Core.Domain;
using ImageCare.Core.Domain.Media;
using ImageCare.Core.Services.FileOperationsService;
using ImageCare.Core.Services.FileSystemImageService;
using ImageCare.Core.Services.FolderService;
using ImageCare.Mvvm;

using Prism.Regions;

using Serilog;

namespace ImageCare.UI.Avalonia.ViewModels;

internal class MainImageViewModel : ViewModelBase
{
    private readonly IFileSystemImageService _imageService;
    private readonly IFolderService _folderService;
    private readonly IFileOperationsService _fileOperationsService;
    private readonly ILogger _logger;
    private readonly SynchronizationContext _synchronizationContext;
    private Bitmap? _mainBitmap;

    private CompositeDisposable? _compositeDisposable;
    private double _rotationAngle;

    public MainImageViewModel(IFileSystemImageService imageService,
                              IFolderService folderService,
                              IFileOperationsService fileOperationsService,
                              ILogger logger,
                              SynchronizationContext synchronizationContext)
    {
        _imageService = imageService;
        _folderService = folderService;
        _fileOperationsService = fileOperationsService;
        _logger = logger;
        _synchronizationContext = synchronizationContext;
    }

    public Bitmap? MainBitmap
    {
        get => _mainBitmap;
        set => SetProperty(ref _mainBitmap, value);
    }

    public ICommand? ResetMatrixCommand { get; set; }

    public double RotationAngle
    {
        get => _rotationAngle;
        set => SetProperty(ref _rotationAngle, value);
    }

    /// <inheritdoc />
    public override void OnNavigatedTo(NavigationContext navigationContext)
    {
        _compositeDisposable = new CompositeDisposable
        {
            _folderService.FileSystemItemSelected.Subscribe(OnFolderSelected),
            _fileOperationsService.ImagePreviewSelected.Throttle(TimeSpan.FromMilliseconds(150))
                                  .ObserveOn(_synchronizationContext)
                                  .Subscribe(OnImagePreviewSelected)
        };

        if (navigationContext.Parameters["imagePreview"] is SelectedMediaPreview imagePreview)
        {
            OnImagePreviewSelected(imagePreview);
        }
    }

    /// <inheritdoc />
    public override void OnNavigatedFrom(NavigationContext navigationContext)
    {
        _compositeDisposable?.Dispose();
    }

    private void OnImagePreviewSelected(SelectedMediaPreview imagePreview)
    {
        if (imagePreview == MediaPreview.Empty)
        {
            ClearPreview();

            return;
        }

        ResetMatrixCommand?.Execute(null);

        _ = LoadImageAsync(imagePreview);
    }

    private void OnFolderSelected(DirectoryModel item)
    {
        ClearPreview();
    }

    private void ClearPreview()
    {
        MainBitmap = null;
    }

    private async Task LoadImageAsync(MediaPreview imagePreview)
    {
        try
        {
            await using (var imageStream = await _imageService.GetJpegImageStreamAsync(imagePreview, MediaPreviewSize.Large))
            {
                var metadata = await _imageService.GetMediaMetadataAsync(imagePreview);

                RotationAngle = metadata.Orientation.ToRotationAngle();

                MainBitmap = await Task.Run(() => new Bitmap(imageStream));
            }
        }
        catch (Exception exception)
        {
            _logger.Error(exception, $"Unexpected exception during loading main image {imagePreview.Url}");
        }
    }
}
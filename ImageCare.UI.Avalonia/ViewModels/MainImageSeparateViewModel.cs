using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

using Avalonia.Media.Imaging;

using ImageCare.Core.Domain.Media;
using ImageCare.Core.Domain.Preview;
using ImageCare.Core.Services.MediaPreviewOperationsService;
using ImageCare.Core.Services.MediaPreviewService;

using Serilog;

namespace ImageCare.UI.Avalonia.ViewModels;

internal class MainImageSeparateViewModel : ViewModelBase
{
    private readonly IMediaPreviewService _imageService;
    private readonly IMediaPreviewOperationsService _fileOperationsService;
    private readonly ILogger _logger;
    private readonly SynchronizationContext _synchronizationContext;
    private Bitmap? _mainBitmap;

    private CompositeDisposable? _compositeDisposable;
    private double _rotationAngle;
    private CancellationTokenSource _imageLoadCts;
    private string _title = string.Empty;

    public MainImageSeparateViewModel(IMediaPreviewService imageService,
                                      IMediaPreviewOperationsService fileOperationsService,
                                      ILogger logger,
                                      SynchronizationContext synchronizationContext)
    {
        _imageService = imageService;
        _fileOperationsService = fileOperationsService;
        _logger = logger;
        _synchronizationContext = synchronizationContext;

        _imageLoadCts = new CancellationTokenSource();
    }

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    public Bitmap? MainBitmap
    {
        get => _mainBitmap;
        private set
        {
            if (_mainBitmap != value)
            {
                SetProperty(ref _mainBitmap, value);
            }
        }
    }

    public ICommand? ResetMatrixCommand { get; set; }

    public double RotationAngle
    {
        get => _rotationAngle;
        set => SetProperty(ref _rotationAngle, value);
    }

    public void Close()
    {
        try
        {
            CancelImageLoading();
            _compositeDisposable?.Dispose();
            MainBitmap = null;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error during separate image window cleanup");
        }
    }

    public void Open()
    {
        try
        {
            _compositeDisposable = new CompositeDisposable
            {
                _fileOperationsService.ImagePreviewSelected
                                      .Throttle(TimeSpan.FromMilliseconds(150))
                                      .ObserveOn(_synchronizationContext)
                                      .Subscribe(OnImagePreviewSelected, OnObservableError)
            };

            var lastImageSelectedPreview = _fileOperationsService.GetLastSelectedMediaPreview();
            if (lastImageSelectedPreview != null)
            {
                OnImagePreviewSelected(lastImageSelectedPreview);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to initialize separate image window");
        }
    }

    private void OnImagePreviewSelected(MediaPreview imagePreview)
    {
        try
        {
            if (imagePreview == MediaPreview.Empty)
            {
                ClearPreview();
                return;
            }

            CancelImageLoading();
            ResetMatrixCommand?.Execute(null);

            _ = LoadImageAsync(imagePreview);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to handle image preview selection");
        }
    }

    private void ClearPreview()
    {
        try
        {
            MainBitmap = null;
            RotationAngle = 0;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to clear preview");
        }
    }

    private async Task LoadImageAsync(MediaPreview imagePreview)
    {
        var cancellationToken = _imageLoadCts.Token;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            await using var imageStream = await _imageService.GetJpegImageStreamAsync(imagePreview, MediaPreviewSize.Large, cancellationToken)
                                                             ;

            cancellationToken.ThrowIfCancellationRequested();

            var metadata = await _imageService.GetMediaMetadataAsync(imagePreview)
                                              ;

            cancellationToken.ThrowIfCancellationRequested();

            RotationAngle = metadata.Orientation.ToRotationAngle();

            var bitmap = await Task.Run(
                                       () =>
                                       {
                                           cancellationToken.ThrowIfCancellationRequested();

                                           return new Bitmap(imageStream);
                                       },
                                       cancellationToken)
                                   ;

            cancellationToken.ThrowIfCancellationRequested();

            MainBitmap = bitmap;
            Title = $"{imagePreview.Title} - {metadata.CreationDateTime} - {metadata.GetString()}";
        }
        catch (OperationCanceledException)
        {
            // Ignored
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load image: {ImagePreviewUrl}", imagePreview.Url);

            ClearPreview();
        }
    }

    private void OnObservableError(Exception ex)
    {
        _logger.Error(ex, "Error in observable subscription");
    }

    private void CancelImageLoading()
    {
        try
        {
            _imageLoadCts.Cancel();
            _imageLoadCts.Dispose();
            _imageLoadCts = new CancellationTokenSource();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to cancel image loading");
        }
    }
}
using System;
using System.Collections.Generic;
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
using ImageCare.UI.Avalonia.Services;

using Mapsui;
using Mapsui.Extensions;
using Mapsui.Layers;
using Mapsui.Projections;
using Mapsui.Styles;
using Mapsui.Tiling;
using Mapsui.Widgets;
using Mapsui.Widgets.InfoWidgets;

using Prism.Dialogs;
using Prism.Navigation.Regions;

using Serilog;

using Location = ImageCare.Core.Domain.Media.Metadata.Location;

namespace ImageCare.UI.Avalonia.ViewModels;

internal class MainImageViewModel : NavigatedViewModelBase
{
    private const string pointLayerName = "photo_point";

    private readonly IMediaPreviewService _imageService;
    private readonly IMediaPreviewOperationsService _fileOperationsService;
    private readonly IClipboardService _clipboardService;
    private readonly ILogger _logger;
    private readonly IDialogService _dialogService;
    private readonly SynchronizationContext _synchronizationContext;
    private Bitmap? _mainBitmap;

    private CompositeDisposable? _compositeDisposable;
    private double _rotationAngle;
    private bool _mapIsEnabled;
    private bool _hasLocation;
    private Map _map;
    private Location _location = Location.Empty;
    private CancellationTokenSource _imageLoadCts;

    public MainImageViewModel(IMediaPreviewService imageService,
                              IMediaPreviewOperationsService fileOperationsService,
                              IClipboardService clipboardService,
                              ILogger logger,
                              IDialogService dialogService,
                              SynchronizationContext synchronizationContext)
    {
        _imageService = imageService;
        _fileOperationsService = fileOperationsService;
        _clipboardService = clipboardService;
        _logger = logger;
        _dialogService = dialogService;
        _synchronizationContext = synchronizationContext;

        _imageLoadCts = new CancellationTokenSource();

        CopyLocationToClipboardCommand = CreateAsyncCommand(CopyLocationToClipboardAsync);
        OpenInWindowCommand = CreateCommand(OpenInWindow);
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

    public ICommand CopyLocationToClipboardCommand { get; }

    public ICommand OpenInWindowCommand { get; }

    public double RotationAngle
    {
        get => _rotationAngle;
        set => SetProperty(ref _rotationAngle, value);
    }

    public bool MapIsEnabled
    {
        get => _mapIsEnabled;
        set
        {
            if (SetProperty(ref _mapIsEnabled, value) && _mapIsEnabled)
            {
                LocateMap(Location);
            }
        }
    }

    public bool HasLocation
    {
        get => _hasLocation;
        set
        {
            SetProperty(ref _hasLocation, value);
            if (!HasLocation)
            {
                MapIsEnabled = false;
            }
        }
    }

    public Map Map
    {
        get => _map;
        set => SetProperty(ref _map, value);
    }

    public Location Location
    {
        get => _location;
        set => SetProperty(ref _location, value);
    }

    public override void OnNavigatedTo(NavigationContext navigationContext)
    {
        try
        {
            Map = CreateMap();

            _compositeDisposable = new CompositeDisposable
            {
                _fileOperationsService.ImagePreviewSelected
                                      .Throttle(TimeSpan.FromMilliseconds(150))
                                      .ObserveOn(_synchronizationContext)
                                      .Subscribe(OnImagePreviewSelected, OnObservableError)
            };

            if (navigationContext.Parameters["imagePreview"] is SelectedMediaPreview imagePreview)
            {
                OnImagePreviewSelected(imagePreview);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to initialize MainImageViewModel");
        }
    }

    /// <inheritdoc />
    public override void OnNavigatedFrom(NavigationContext navigationContext)
    {
        try
        {
            CancelImageLoading();
            _compositeDisposable?.Dispose();
            MainBitmap = null;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error during MainImageViewModel cleanup");
        }
    }

    private void OpenInWindow()
    {
        try
        {
            _dialogService.Show("imageViewer", null, null, "childWindow");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to open settings window");
        }
    }

    private void OnImagePreviewSelected(SelectedMediaPreview imagePreview)
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
            HasLocation = false;
            Location = Location.Empty;
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
                                                             .ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();

            var metadata = await _imageService.GetMediaMetadataAsync(imagePreview)
                                              .ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();

            HasLocation = metadata.Location != Location.Empty;
            Location = metadata.Location;
            RotationAngle = metadata.Orientation.ToRotationAngle();

            var bitmap = await Task.Run(
                                       () =>
                                       {
                                           cancellationToken.ThrowIfCancellationRequested();

                                           return new Bitmap(imageStream);
                                       },
                                       cancellationToken)
                                   .ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();

            MainBitmap = bitmap;
            LocateMap(metadata.Location);
        }
        catch (OperationCanceledException)
        {
            // Ignored
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load image: {Url}", imagePreview.Url);

            ClearPreview();
        }
    }

    private void LocateMap(Location location)
    {
        try
        {
            if (MapIsEnabled && location != Location.Empty)
            {
                if (Map.Navigator.Viewport.Width == 0)
                {
                    Map.ViewportInitialized += (s, e) => LocateMap(location);

                    return;
                }

                LoggingWidget.ShowLoggingInMap = ActiveMode.No;

                var sphericalMercatorCoordinate = SphericalMercator.FromLonLat(location.Longitude, location.Latitude).ToMPoint();
                Map.Navigator.CenterOnAndZoomTo(sphericalMercatorCoordinate, Map.Navigator.Viewport.Resolution);

                Map.Layers.Remove(l => l.Name == pointLayerName);

                Map.Layers.Add(CreatePointLayer());

                var extent = new MRect(sphericalMercatorCoordinate.X - 1000, sphericalMercatorCoordinate.Y - 1000, sphericalMercatorCoordinate.X + 1000, sphericalMercatorCoordinate.Y + 1000);
                Map.Navigator.ZoomToBox(extent);

                Map.Refresh();
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to locate map for coordinates: {Lat}, {Lon}", location.Latitude, location.Longitude);
        }
    }

    private static Map CreateMap()
    {
        var map = new Map();

        var widgets = map.GetWidgetsOfMapAndLayers();
        foreach (var widget in widgets)
        {
            widget.Enabled = false;
        }

        map.Layers.Add(OpenStreetMap.CreateTileLayer());
        map.Navigator.RotationLock = true;

        return map;
    }

    private MemoryLayer CreatePointLayer()
    {
        try
        {
            return new MemoryLayer
            {
                Name = pointLayerName,
                Features = GetPhotoPointFromEmbeddedResource(),
                Style = CreateBitmapStyle()
            };
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to create point layer");
            return new MemoryLayer { Name = pointLayerName };
        }
    }

    private ImageStyle? CreateBitmapStyle()
    {
        try
        {
            var bitmapHeight = 300;

            return new ImageStyle
            {
                Image = "embedded://ImageCare.UI.Avalonia.Assets.birdPoint.png",
                SymbolScale = 0.20,
                Offset = new Offset(0, bitmapHeight * 0.5)
            };
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to create bitmap style");
            return null;
        }
    }

    private IEnumerable<IFeature> GetPhotoPointFromEmbeddedResource()
    {
        try
        {
            var feature = new PointFeature(SphericalMercator.FromLonLat(Location.Longitude, Location.Latitude).ToMPoint());

            return new List<IFeature>(1) { feature };
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to create photo point feature");
            return new List<IFeature>();
        }
    }

    private async Task CopyLocationToClipboardAsync()
    {
        try
        {
            await _clipboardService.CopyToClipboardAsync(Location.ToString());
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to copy location to clipboard");
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
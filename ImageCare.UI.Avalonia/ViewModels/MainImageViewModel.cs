using System;
using System.Collections.Generic;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

using Avalonia.Controls;
using Avalonia.Media.Imaging;

using CommunityToolkit.Mvvm.Input;

using ImageCare.Core.Domain.Folders;
using ImageCare.Core.Domain.Media;
using ImageCare.Core.Domain.Preview;
using ImageCare.Core.Services.FileOperationsService;
using ImageCare.Core.Services.FileSystemImageService;
using ImageCare.Core.Services.FolderService;
using ImageCare.UI.Avalonia.Controls;
using ImageCare.UI.Avalonia.Services;

using Mapsui;
using Mapsui.Extensions;
using Mapsui.Layers;
using Mapsui.Projections;
using Mapsui.Styles;
using Mapsui.Tiling;

using Prism.Commands;
using Prism.Ioc;
using Prism.Regions;

using Serilog;
using static System.Net.Mime.MediaTypeNames;

using Location = ImageCare.Core.Domain.Media.Metadata.Location;

namespace ImageCare.UI.Avalonia.ViewModels;

internal class MainImageViewModel : NavigatedViewModelBase
{
	private const string pointLayerName = "photo_point";

	private readonly IFileSystemImageService _imageService;
	private readonly IFolderService _folderService;
	private readonly IFileOperationsService _fileOperationsService;
	private readonly IClipboardService _clipboardService;
	private readonly ILogger _logger;
	private readonly IContainerProvider _containerProvider;
	private readonly SynchronizationContext _synchronizationContext;
	private Bitmap? _mainBitmap;

	private CompositeDisposable? _compositeDisposable;
	private double _rotationAngle;
	private bool _mapIsEnabled;
	private bool _hasLocation;
	private Map _map;
	private Location _location = Location.Empty;

	public MainImageViewModel(IFileSystemImageService imageService,
	                          IFolderService folderService,
	                          IFileOperationsService fileOperationsService,
	                          IClipboardService clipboardService,
							  ILogger logger,
	                          IContainerProvider containerProvider,
	                          SynchronizationContext synchronizationContext)
	{
		_imageService = imageService;
		_folderService = folderService;
		_fileOperationsService = fileOperationsService;
		_clipboardService = clipboardService;
		_logger = logger;
		_containerProvider = containerProvider;
		_synchronizationContext = synchronizationContext;
		CopyLocationToClipboardCommand = new AsyncRelayCommand(CopyLocationToClipboardAsync);
	}

	public Bitmap? MainBitmap
	{
		get => _mainBitmap;
		set => SetProperty(ref _mainBitmap, value);
	}

	public ICommand? ResetMatrixCommand { get; set; }

	public ICommand CopyLocationToClipboardCommand { get; }

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

	/// <inheritdoc />
	public override void OnNavigatedTo(NavigationContext navigationContext)
	{
		Map = CreateMap();
		var mapMediator = _containerProvider.Resolve<MapControlMediator>();
		mapMediator.SetMap(Map);

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
				HasLocation = metadata.Location != Location.Empty;
				Location = metadata.Location;
				RotationAngle = metadata.Orientation.ToRotationAngle();

				MainBitmap = await Task.Run(() => new Bitmap(imageStream));

				LocateMap(metadata.Location);
			}
		}
		catch (Exception exception)
		{
			_logger.Error(exception, $"Unexpected exception during loading main image {imagePreview.Url}");
		}
	}

	private void LocateMap(Location location)
	{
		if (MapIsEnabled && location != Location.Empty)
		{
			var sphericalMercatorCoordinate = SphericalMercator.FromLonLat(location.Longitude, location.Latitude).ToMPoint();
			Map.Navigator.CenterOnAndZoomTo(sphericalMercatorCoordinate, Map.Navigator.Viewport.Resolution);

			Map.Layers.Remove(l => l.Name == pointLayerName);

			Map.Layers.Add(CreatePointLayer());
		}
	}

	private static Map CreateMap()
	{
		var map = new Map();

		map.Layers.Add(OpenStreetMap.CreateTileLayer());
		map.Navigator.RotationLock = true;

		return map;
	}

	private MemoryLayer CreatePointLayer()
	{
		return new MemoryLayer
		{
			Name = pointLayerName,
			Features = GetPhotoPointFromEmbeddedResource(),
			Style = CreateBitmapStyle()
		};
	}

	private SymbolStyle CreateBitmapStyle()
	{
		var bitmapId = LoadBitmapId(GetType());
		var bitmapHeight = 300;
		return new SymbolStyle { BitmapId = bitmapId, SymbolScale = 0.20, SymbolOffset = new Offset(0, bitmapHeight * 0.5) };
	}

	private int LoadBitmapId(Type typeInAssemblyOfEmbeddedResource)
	{
		var fullName = "ImageCare.UI.Avalonia.Assets.birdPoint.png";
		if (BitmapRegistry.Instance.TryGetBitmapId(fullName, out var bitmapId))
		{
			return bitmapId;
		}

		var assembly = typeInAssemblyOfEmbeddedResource.GetTypeInfo().Assembly;
		var stream = assembly.GetManifestResourceStream(fullName);
		if (stream == null)
		{
			return bitmapId;
		}

		bitmapId = BitmapRegistry.Instance.Register(stream, fullName);
		return bitmapId;

	}

	private IEnumerable<IFeature> GetPhotoPointFromEmbeddedResource()
	{
		var feature = new PointFeature(SphericalMercator.FromLonLat(Location.Longitude, Location.Latitude).ToMPoint());

		return new List<IFeature>(1) { feature };
	}

	private async Task CopyLocationToClipboardAsync()
	{
		await _clipboardService.CopyToClipboardAsync(Location.ToString());
	}
}
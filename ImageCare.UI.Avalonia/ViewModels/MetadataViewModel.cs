using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;

using ImageCare.Core.Domain.Preview;
using ImageCare.Core.Services.MediaPreviewOperationsService;
using ImageCare.Core.Services.MediaPreviewService;
using ImageCare.UI.Avalonia.ViewModels.Domain;

using Prism.Commands;

using Serilog;

namespace ImageCare.UI.Avalonia.ViewModels;

internal sealed class MetadataViewModel : ViewModelBase
{
	private readonly IMediaPreviewService _imageService;
	private readonly IMediaPreviewOperationsService _fileOperationsService;
	private readonly ILogger _logger;

	public MetadataViewModel(IMediaPreviewService imageService,
	                         IMediaPreviewOperationsService fileOperationsService,
	                         ILogger logger)
	{
		_imageService = imageService;
		_fileOperationsService = fileOperationsService;
		_logger = logger;

		MetadataList = new ObservableCollection<TagDescriptionViewModel>();
		OnViewLoadedCommand = new DelegateCommand(OnViewLoaded);
	}

	public ObservableCollection<TagDescriptionViewModel> MetadataList { get; }

	public ICommand OnViewLoadedCommand { get; }

	private void OnViewLoaded()
	{
		try
		{
			MetadataList.Clear();

			var lastSelectedMediaPreview = _fileOperationsService.GetLastSelectedMediaPreview();

			if (lastSelectedMediaPreview == null)
			{
				return;
			}

			_ = FillMetadataListAsync(lastSelectedMediaPreview);
		}
		catch (Exception ex)
		{
			_logger.Error(ex, "Error during metadata view initialization");
		}
	}

	private async Task FillMetadataListAsync(MediaPreview lastSelectedMediaPreview)
	{
		try
		{
			var metadata = await _imageService.GetMediaMetadataAsync(lastSelectedMediaPreview);

			foreach (var item in metadata.AllMetadata)
			{
				try
				{
					MetadataList.Add(new TagDescriptionViewModel(item.Key, item.Value));
				}
				catch (Exception ex)
				{
					_logger.Error(ex, "Failed to add metadata item with key: {Key}", item.Key);
				}
			}
		}
		catch (Exception ex)
		{
			_logger.Error(ex, "Failed to load metadata for: {Url}", lastSelectedMediaPreview.Url);
		}
	}
}
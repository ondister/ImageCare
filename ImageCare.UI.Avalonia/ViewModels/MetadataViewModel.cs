using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
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
    private readonly List<TagDescriptionViewModel> _allMetadata = [];
    private string _searchText = string.Empty;
    private int _loadGeneration;

    public MetadataViewModel(IMediaPreviewService imageService,
                             IMediaPreviewOperationsService fileOperationsService,
                             ILogger logger)
    {
        _imageService = imageService;
        _fileOperationsService = fileOperationsService;
        _logger = logger;

        MetadataList = new ObservableCollection<TagDescriptionViewModel>();
        OnViewLoadedCommand = new DelegateCommand(OnViewLoaded);
        ClearSearchCommand = new DelegateCommand(() => SearchText = string.Empty);
    }

    public ObservableCollection<TagDescriptionViewModel> MetadataList { get; }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value ?? string.Empty))
            {
                RaisePropertyChanged(nameof(HasSearchText));
                ApplyFilter();
            }
        }
    }

    public bool HasSearchText => !string.IsNullOrWhiteSpace(SearchText);

    public ICommand OnViewLoadedCommand { get; }

    public ICommand ClearSearchCommand { get; }

    private void OnViewLoaded()
    {
        try
        {
            var generation = ++_loadGeneration;
            _allMetadata.Clear();
            MetadataList.Clear();
            SearchText = string.Empty;

            var lastSelectedMediaPreview = _fileOperationsService.GetLastSelectedMediaPreview();
            if (lastSelectedMediaPreview == null)
            {
                return;
            }

            _ = FillMetadataListAsync(lastSelectedMediaPreview, generation);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error during metadata view initialization");
        }
    }

    private async Task FillMetadataListAsync(MediaPreview lastSelectedMediaPreview, int generation)
    {
        try
        {
            var metadata = await _imageService.GetMediaMetadataAsync(lastSelectedMediaPreview);
            if (generation != _loadGeneration)
            {
                return;
            }

            var items = metadata.AllMetadata
                .Select(item => new TagDescriptionViewModel(item.Key, item.Value))
                .OrderBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            _allMetadata.Clear();
            _allMetadata.AddRange(items);
            ApplyFilter();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load metadata for: {Url}", lastSelectedMediaPreview.Url);
        }
    }

    private void ApplyFilter()
    {
        var query = SearchText.Trim();
        var items = query.Length == 0
            ? _allMetadata
            : _allMetadata
                .Where(item =>
                    item.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    item.Description.Contains(query, StringComparison.OrdinalIgnoreCase));

        MetadataList.Clear();
        foreach (var item in items)
        {
            MetadataList.Add(item);
        }
    }
}

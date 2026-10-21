using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

using ImageCare.Core.Domain.Annotations;
using ImageCare.Core.Domain.Folders;
using ImageCare.Core.Domain.MediaFormats;
using ImageCare.Core.Domain.Notification;
using ImageCare.Core.Domain.Preview;
using ImageCare.Core.Services.ConfigurationService;
using ImageCare.Core.Services.MediaAnnotationsService;
using ImageCare.Core.Services.MediaPreviewOperationsService;
using ImageCare.Core.Services.MediaPreviewService;
using ImageCare.Core.Services.NotificationService;
using ImageCare.UI.Avalonia.Localization;
using ImageCare.UI.Avalonia.ViewModels.Domain;

using Serilog;

namespace ImageCare.UI.Avalonia.ViewModels;

public sealed class MediaSearchViewModel : ViewModelBase
{
    private readonly IMediaAnnotationsService _annotationsService;
    private readonly IConfigurationService _configurationService;
    private readonly IMediaPreviewService _previewService;
    private readonly IMediaPreviewOperationsService _previewOperations;
    private readonly INotificationService _notificationService;
    private readonly LocalizationManager _localization;
    private readonly ILogger _logger;
    private readonly Action? _navigationRequested;
    private string _text = string.Empty;
    private string _tagsText = string.Empty;
    private bool _useMinimumRating;
    private bool _useMaximumRating;
    private int _minimumRating;
    private int _maximumRating = 5;
    private bool _matchAllTags = true;
    private bool _isSearching;
    private string _statusText;
    private MediaSearchResultViewModel? _selectedResult;

    public MediaSearchViewModel(
        IMediaAnnotationsService annotationsService,
        IConfigurationService configurationService,
        IMediaPreviewService previewService,
        IMediaPreviewOperationsService previewOperations,
        INotificationService notificationService,
        LocalizationManager localization,
        ILogger logger,
        Action? navigationRequested = null)
    {
        _annotationsService = annotationsService;
        _configurationService = configurationService;
        _previewService = previewService;
        _previewOperations = previewOperations;
        _notificationService = notificationService;
        _localization = localization;
        _logger = logger;
        _navigationRequested = navigationRequested;
        _statusText = localization["Search_Ready"];
        SearchCommand = CreateAsyncCommand(SearchAsync);
        ShowSelectedCommand = CreateAsyncCommand(ShowSelectedAsync, () => SelectedResult != null)
            .ObservesProperty(() => SelectedResult);
        OpenSelectedCommand = CreateAsyncCommand(OpenSelectedAsync, () => SelectedResult != null)
            .ObservesProperty(() => SelectedResult);
    }

    public string Title => _localization["Search_Title"];

    public ObservableCollection<MediaSearchResultViewModel> Results { get; } = new();

    public string Text
    {
        get => _text;
        set => SetProperty(ref _text, value ?? string.Empty);
    }

    public string TagsText
    {
        get => _tagsText;
        set => SetProperty(ref _tagsText, value ?? string.Empty);
    }

    public bool UseMinimumRating
    {
        get => _useMinimumRating;
        set => SetProperty(ref _useMinimumRating, value);
    }

    public bool UseMaximumRating
    {
        get => _useMaximumRating;
        set => SetProperty(ref _useMaximumRating, value);
    }

    public int MinimumRating
    {
        get => _minimumRating;
        set => SetProperty(ref _minimumRating, Math.Clamp(value, 0, 5));
    }

    public int MaximumRating
    {
        get => _maximumRating;
        set => SetProperty(ref _maximumRating, Math.Clamp(value, 0, 5));
    }

    public bool MatchAllTags
    {
        get => _matchAllTags;
        set => SetProperty(ref _matchAllTags, value);
    }

    public bool IsSearching
    {
        get => _isSearching;
        private set => SetProperty(ref _isSearching, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public MediaSearchResultViewModel? SelectedResult
    {
        get => _selectedResult;
        set => SetProperty(ref _selectedResult, value);
    }

    public ICommand SearchCommand { get; }

    public ICommand ShowSelectedCommand { get; }

    public ICommand OpenSelectedCommand { get; }

    private async Task SearchAsync(IProgress<int> progress, CancellationToken cancellationToken)
    {
        try
        {
            if (UseMinimumRating && UseMaximumRating && MinimumRating > MaximumRating)
            {
                StatusText = _localization["Search_InvalidRatingRange"];
                return;
            }

            IsSearching = true;
            StatusText = _localization["Search_Searching"];
            Results.Clear();
            SelectedResult = null;
            var roots = (_configurationService.Configuration.Value.MediaSearchRootPaths ?? [])
                .Where(root => !string.IsNullOrWhiteSpace(root))
                .Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
                .ToArray();
            if (roots.Length == 0)
            {
                StatusText = _localization["Search_NoRoots"];
                return;
            }

            var tags = TagsText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var found = new System.Collections.Generic.Dictionary<string, IndexedMediaAnnotations>(
                OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            foreach (var root in roots)
            {
                var query = new MediaAnnotationsSearchQuery(
                    string.IsNullOrWhiteSpace(Text) ? null : Text.Trim(),
                    RootPath: root,
                    MinimumRating: UseMinimumRating ? MinimumRating : null,
                    MaximumRating: UseMaximumRating ? MaximumRating : null,
                    Tags: tags,
                    MatchAllTags: MatchAllTags,
                    Limit: 500);
                foreach (var result in await _annotationsService.SearchAsync(query, cancellationToken))
                {
                    found[result.MediaPath] = result;
                }
            }

            foreach (var result in found.Values
                         .OrderByDescending(item => item.Annotations.Rating)
                         .ThenBy(item => item.MediaPath, StringComparer.OrdinalIgnoreCase))
            {
                Results.Add(new MediaSearchResultViewModel(result));
            }

            StatusText = Results.Count == 0
                ? _localization["Search_NoResults"]
                : _localization.Get("Search_ResultCount", Results.Count);
        }
        catch (OperationCanceledException)
        {
            StatusText = _localization["Search_Cancelled"];
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Media index search failed");
            StatusText = _localization["Search_Failed"];
        }
        finally
        {
            IsSearching = false;
        }
    }

    private Task ShowSelectedAsync(IProgress<int> progress, CancellationToken cancellationToken) =>
        OpenSelectedAsync(navigateToFile: false, cancellationToken);

    private Task OpenSelectedAsync(IProgress<int> progress, CancellationToken cancellationToken) =>
        OpenSelectedAsync(navigateToFile: true, cancellationToken);

    private async Task OpenSelectedAsync(bool navigateToFile, CancellationToken cancellationToken)
    {
        if (SelectedResult is not { } selected)
        {
            return;
        }

        try
        {
            if (!File.Exists(selected.Path))
            {
                _notificationService.SendNotification(
                    new ErrorNotification(_localization["Search_FileMissing"], selected.Path));
                return;
            }

            var preview = await _previewService.GetMediaPreviewAsync(selected.Path);
            cancellationToken.ThrowIfCancellationRequested();
            if (preview == null || preview.MediaFormat.MediaType != MediaType.Image)
            {
                _notificationService.SendNotification(
                    new ErrorNotification(_localization["Search_OpenFailed"], selected.Path));
                return;
            }

            var lastPanel = (_previewOperations.GetLastSelectedMediaPreview() as SelectedMediaPreview)?.FileManagerPanel
                ?? FileManagerPanel.Left;
            var selectedPreview = new SelectedMediaPreview(preview, lastPanel);
            if (navigateToFile)
            {
                _previewOperations.RequestMediaPreviewNavigation(selectedPreview);
                _navigationRequested?.Invoke();
            }
            else
            {
                _previewOperations.SetSelectedPreview(selectedPreview);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to open media search result {Path}", selected.Path);
            _notificationService.SendNotification(
                new ErrorNotification(_localization["Search_OpenFailed"], selected.Path));
        }
    }
}

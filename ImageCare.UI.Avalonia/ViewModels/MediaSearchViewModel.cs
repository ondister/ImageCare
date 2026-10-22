using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

using Avalonia.Media.Imaging;
using Avalonia.Threading;

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
    // Matches the widest allowed preview column so widened thumbnails stay sharp.
    private const int ThumbnailDecodeWidth = 260;
    private static readonly SemaphoreSlim ThumbnailDecodeGate = new(3, 3);

    private readonly IMediaAnnotationsService _annotationsService;
    private readonly IConfigurationService _configurationService;
    private readonly IMediaPreviewService _previewService;
    private readonly IMediaPreviewOperationsService _previewOperations;
    private readonly INotificationService _notificationService;
    private readonly LocalizationManager _localization;
    private readonly ILogger _logger;
    private string _text = string.Empty;
    private string _tagSearchText = string.Empty;
    private string[] _availableTags = [];
    private int _minimumRatingIndex;
    private int _maximumRatingIndex;
    private bool _matchAllTags = true;
    private bool _isSearching;
    private string _statusText;
    private MediaSearchResultViewModel? _selectedResult;
    private CancellationTokenSource _thumbnailCancellation = new();

    public MediaSearchViewModel(
        IMediaAnnotationsService annotationsService,
        IConfigurationService configurationService,
        IMediaPreviewService previewService,
        IMediaPreviewOperationsService previewOperations,
        INotificationService notificationService,
        LocalizationManager localization,
        ILogger logger)
    {
        _annotationsService = annotationsService;
        _configurationService = configurationService;
        _previewService = previewService;
        _previewOperations = previewOperations;
        _notificationService = notificationService;
        _localization = localization;
        _logger = logger;
        _statusText = localization["Search_Ready"];
        SearchCommand = CreateAsyncCommand(SearchAsync);
        ShowSelectedCommand = CreateAsyncCommand(ShowSelectedAsync, () => SelectedResult != null)
            .ObservesProperty(() => SelectedResult);
        OpenSelectedCommand = CreateAsyncCommand(OpenSelectedAsync, () => SelectedResult != null)
            .ObservesProperty(() => SelectedResult);
        RemoveTagCommand = CreateCommand<string>(RemoveSelectedTag);
        AddTagCommand = CreateCommand<string>(AddSelectedTag);
        _ = LoadAvailableTagsAsync();
    }

    public string Title => _localization["Search_Title"];

    public ObservableCollection<MediaSearchResultViewModel> Results { get; } = new();

    public string Text
    {
        get => _text;
        set => SetProperty(ref _text, value ?? string.Empty);
    }

    /// <summary>
    /// Text typed into the tag field. It only filters <see cref="TagSuggestions" />; the actual
    /// filter uses <see cref="SelectedTags" />, which is filled by picking a suggestion or pressing Enter.
    /// </summary>
    public string TagSearchText
    {
        get => _tagSearchText;
        set
        {
            if (SetProperty(ref _tagSearchText, value ?? string.Empty))
            {
                RefreshTagSuggestions();
            }
        }
    }

    /// <summary>
    /// Tags that can be picked right now: every known tag matching the typed text that is not
    /// selected yet. Showing them keeps the user from typing tags blind.
    /// </summary>
    public ObservableCollection<string> TagSuggestions { get; } = new();

    public ObservableCollection<string> SelectedTags { get; } = new();

    public bool HasSelectedTags => SelectedTags.Count > 0;

    public bool HasAvailableTags => _availableTags.Length > 0;

    public bool HasTagSuggestions => TagSuggestions.Count > 0;

    public bool NoMatchingTags => HasAvailableTags && !HasTagSuggestions;

    /// <summary>
    /// Minimum rating filter: 0 means "any rating", 1..5 means "rating is at least N".
    /// </summary>
    public int MinimumRatingIndex
    {
        get => _minimumRatingIndex;
        set => SetProperty(ref _minimumRatingIndex, Math.Clamp(value, 0, 5));
    }

    /// <summary>
    /// Maximum rating filter: 0 means "any rating", 1..5 means "rating is at most N".
    /// </summary>
    public int MaximumRatingIndex
    {
        get => _maximumRatingIndex;
        set => SetProperty(ref _maximumRatingIndex, Math.Clamp(value, 0, 5));
    }

    public bool MatchAllTags => _matchAllTags;

    /// <summary>
    /// Tag matching mode: 0 matches every tag, 1 matches at least one tag.
    /// </summary>
    public int TagMatchModeIndex
    {
        get => _matchAllTags ? 0 : 1;
        set
        {
            var matchAllTags = value <= 0;
            if (_matchAllTags == matchAllTags)
            {
                return;
            }

            _matchAllTags = matchAllTags;
            RaisePropertyChanged();
            RaisePropertyChanged(nameof(MatchAllTags));
        }
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

    public ICommand RemoveTagCommand { get; }

    public ICommand AddTagCommand { get; }

    private async Task SearchAsync(IProgress<int> progress, CancellationToken cancellationToken)
    {
        try
        {
            var minimumRating = MinimumRatingIndex > 0 ? MinimumRatingIndex : (int?)null;
            var maximumRating = MaximumRatingIndex > 0 ? MaximumRatingIndex : (int?)null;
            if (minimumRating is { } minimum && maximumRating is { } maximum && minimum > maximum)
            {
                StatusText = _localization["Search_InvalidRatingRange"];
                return;
            }

            IsSearching = true;
            StatusText = _localization["Search_Searching"];
            Results.Clear();
            SelectedResult = null;
            var thumbnailToken = ResetThumbnailLoading();
            var roots = (_configurationService.Configuration.Value.MediaSearchRootPaths ?? [])
                .Where(root => !string.IsNullOrWhiteSpace(root))
                .Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
                .ToArray();
            if (roots.Length == 0)
            {
                StatusText = _localization["Search_NoRoots"];
                return;
            }

            var tags = SelectedTags.ToArray();
            var found = new System.Collections.Generic.Dictionary<string, IndexedMediaAnnotations>(
                OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            foreach (var root in roots)
            {
                var query = new MediaAnnotationsSearchQuery(
                    string.IsNullOrWhiteSpace(Text) ? null : Text.Trim(),
                    RootPath: root,
                    MinimumRating: minimumRating,
                    MaximumRating: maximumRating,
                    Tags: tags,
                    MatchAllTags: _matchAllTags,
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
                Results.Add(new MediaSearchResultViewModel(
                    result,
                    path => LoadThumbnailAsync(path, thumbnailToken)));
            }

            StatusText = Results.Count == 0
                ? _localization["Search_NoResults"]
                : _localization.Get("Search_ResultCount", Results.Count);
            _ = LoadAvailableTagsAsync();
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

    /// <summary>
    /// Moves whatever is currently typed into the tag chips. Invoked when Enter is pressed in the tag field.
    /// </summary>
    public void AddTagInput() => AddSelectedTag(TagSearchText);

    private void AddSelectedTag(string? tag)
    {
        var normalized = MediaAnnotations.NormalizeTag(tag ?? string.Empty);
        if (normalized.Length > 0 &&
            !SelectedTags.Any(selected => selected.Equals(normalized, StringComparison.OrdinalIgnoreCase)))
        {
            SelectedTags.Add(normalized);
            RaisePropertyChanged(nameof(HasSelectedTags));
        }

        TagSearchText = string.Empty;
        RefreshTagSuggestions();
    }

    private void RemoveSelectedTag(string? tag)
    {
        if (tag == null || !SelectedTags.Remove(tag))
        {
            return;
        }

        RaisePropertyChanged(nameof(HasSelectedTags));
        RefreshTagSuggestions();
    }

    private void RefreshTagSuggestions()
    {
        var query = TagSearchText.Trim();
        var matches = _availableTags
            .Where(tag => query.Length == 0 || MediaAnnotations.MatchesTagQuery(tag, query))
            .Where(tag => !SelectedTags.Any(selected => selected.Equals(tag, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (!TagSuggestions.SequenceEqual(matches, StringComparer.Ordinal))
        {
            TagSuggestions.Clear();
            foreach (var tag in matches)
            {
                TagSuggestions.Add(tag);
            }
        }

        RaisePropertyChanged(nameof(HasTagSuggestions));
        RaisePropertyChanged(nameof(NoMatchingTags));
    }

    private async Task LoadAvailableTagsAsync()
    {
        try
        {
            var indexedTags = await _annotationsService.GetAllTagsAsync();
            var pinnedTags = _configurationService.Configuration.Value.PinnedTags ?? [];
            var tags = pinnedTags.Concat(indexedTags)
                .Select(MediaAnnotations.NormalizeTag)
                .Where(tag => tag.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(tag => tag, StringComparer.Create(_localization.Culture, ignoreCase: true))
                .ThenBy(tag => tag, StringComparer.Ordinal)
                .ToArray();
            Dispatcher.UIThread.Post(() =>
            {
                _availableTags = tags;
                RaisePropertyChanged(nameof(HasAvailableTags));
                RefreshTagSuggestions();
            });
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to load available search tags");
        }
    }

    private CancellationToken ResetThumbnailLoading()
    {
        var previous = _thumbnailCancellation;
        _thumbnailCancellation = new CancellationTokenSource();
        // The previous source is cancelled but not disposed: pending thumbnail loads may still read
        // its token and would otherwise observe ObjectDisposedException.
        previous.Cancel();
        return _thumbnailCancellation.Token;
    }

    private async Task<Bitmap?> LoadThumbnailAsync(string path, CancellationToken token)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var preview = await _previewService.GetMediaPreviewAsync(path, token).ConfigureAwait(false);
            if (preview == null)
            {
                return null;
            }

            await ThumbnailDecodeGate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                await using var stream = await _previewService
                                                .GetJpegImageStreamAsync(preview, MediaPreviewSize.Small, token)
                                                .ConfigureAwait(false);
                return await Task.Run(
                                 () => Bitmap.DecodeToWidth(
                                     stream,
                                     ThumbnailDecodeWidth,
                                     BitmapInterpolationMode.MediumQuality),
                                 token)
                             .ConfigureAwait(false);
            }
            finally
            {
                ThumbnailDecodeGate.Release();
            }
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to load search result thumbnail for {Path}", path);
            return null;
        }
    }
}

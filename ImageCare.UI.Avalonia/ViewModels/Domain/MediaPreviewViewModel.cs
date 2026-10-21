using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Media.Imaging;

using ImageCare.Core.Domain.Annotations;
using ImageCare.Core.Domain.Media.Metadata;
using ImageCare.Core.Domain.MediaFormats;
using ImageCare.Core.Domain.Notification;
using ImageCare.Core.Domain.Preview;
using ImageCare.Core.Services.FileAssociationsService;
using ImageCare.Core.Services.MediaPreviewOperationsService;
using ImageCare.Core.Services.MediaPreviewService;
using ImageCare.Core.Services.NotificationService;
using ImageCare.UI.Avalonia.Localization;

using MapsterMapper;

using Serilog;

namespace ImageCare.UI.Avalonia.ViewModels.Domain;

internal class MediaPreviewViewModel : ViewModelBase, IComparable<MediaPreviewViewModel>
{
    private const int PreviewDecodeWidth = 512;
    private static readonly SemaphoreSlim PreviewDecodeGate = new(4, 4);

    private readonly SemaphoreSlim _previewLoadGate = new(1, 1);
    private readonly IMediaPreviewService _imageService;
    private readonly IMediaPreviewOperationsService _fileOperationsService;
    private readonly INotificationService _notificationService;
    private readonly IFileAssociationsService _fileAssociationsService;
    private readonly IMapper _mapper;
    private readonly ILogger _logger;
    private readonly LocalizationManager _localization;
    private Bitmap? _previewBitmap;
    private bool _selected;
    private bool _isLoading = true;
    private int _maxImageHeight = 200;
    private string _metadataString = string.Empty;
    private string _dateTimeString = string.Empty;
    private double _rotateAngle;
    private bool _useOpenWith;
    private bool _hasLocation;
    private IMediaMetadata? _metadata;
    private int _rating;
    private IReadOnlyList<string> _tags = Array.Empty<string>();
    private string _annotationState = string.Empty;
    private bool _isAnnotationsSaving;
    private MediaAnnotationsSnapshot? _annotationSnapshot;

    private DateTime _fileDate;
    private string _frameColorCode = "Transparent";
    private bool _isRemoving=false;

    public MediaPreviewViewModel(string? title,
                                 string url,
                                 MediaFormat mediaFormat,
                                 int maxImageHeight,
                                 IMediaPreviewService imageService,
                                 IMediaPreviewOperationsService fileOperationsService,
                                 INotificationService notificationService,
                                 IFileAssociationsService fileAssociationsService,
                                 IMapper mapper,
                                 ILogger logger,
                                 LocalizationManager localization)
    {
        Title = title;
        Url = url;
        MaxImageHeight = maxImageHeight;
        MediaFormat = mediaFormat;

        _imageService = imageService;
        _fileOperationsService = fileOperationsService;
        _notificationService = notificationService;
        _fileAssociationsService = fileAssociationsService;
        _mapper = mapper;
        _logger = logger;
        _localization = localization;

        RemoveImagePreviewCommand = CreateAsyncCommand(RemoveImagePreviewAsync, CanRemoveImagePreview).ObservesProperty(() => IsRemoving);

        OpenWithViewModels = CreateOpenWithItems();
    }

    private bool CanRemoveImagePreview()
    {
        return !IsRemoving;
    }

    public bool IsRemoving
    {
        get => _isRemoving;
        set => SetProperty(ref _isRemoving, value);
    }

    public DateTime FileDate
    {
        get => _fileDate;
        set => SetProperty(ref _fileDate, value);
    }

    public string? Title { get; }

    public string Url { get; }

    public MediaFormat MediaFormat { get; }

    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    public bool Selected
    {
        get => _selected;
        set => SetProperty(ref _selected, value);
    }

    public Bitmap? PreviewBitmap
    {
        get => _previewBitmap;
        set
        {
            if (ReferenceEquals(_previewBitmap, value))
            {
                return;
            }

            var previous = _previewBitmap;
            if (SetProperty(ref _previewBitmap, value))
            {
                previous?.Dispose();
            }
        }
    }

    public void UnloadPreview() => PreviewBitmap = null;

    public int MaxImageHeight
    {
        get => _maxImageHeight;
        set => SetProperty(ref _maxImageHeight, value);
    }

    public ICommand RemoveImagePreviewCommand { get; }

    public string MetadataString
    {
        get => _metadataString;
        set => SetProperty(ref _metadataString, value);
    }

    public string DateTimeString
    {
        get => _dateTimeString;
        set => SetProperty(ref _dateTimeString, value);
    }

    public double RotateAngle
    {
        get => _rotateAngle;
        set => SetProperty(ref _rotateAngle, value);
    }

    public string FrameColorCode
    {
        get => _frameColorCode;
        set => SetProperty(ref _frameColorCode, value);
    }

    public IEnumerable<FileApplicationPairViewModel> OpenWithViewModels { get; }

    public bool UseOpenWith
    {
        get => _useOpenWith;
        set => SetProperty(ref _useOpenWith, value);
    }

    public bool HasLocation
    {
        get => _hasLocation;
        set => SetProperty(ref _hasLocation, value);
    }

    public IMediaMetadata? Metadata
    {
        get => _metadata;
        internal set
        {
            SetProperty(ref _metadata, value);
            HasLocation = _metadata != null && _metadata.Location != Location.Empty;
        }
    }

    public bool IsLoaded => PreviewBitmap != null;

    public int Rating
    {
        get => _rating;
        private set
        {
            if (SetProperty(ref _rating, Math.Clamp(value, 0, 5)))
            {
                RaisePropertyChanged(nameof(RatingStars));
            }
        }
    }

    public string RatingStars => Rating == 0 ? "☆☆☆☆☆" : new string('★', Rating) + new string('☆', 5 - Rating);

    /// <summary>
    /// Annotations (rating and tags) are stored for images only, so video cards must not show rating stars.
    /// </summary>
    public bool SupportsRating => MediaFormat.SupportsAnnotations;

    public IReadOnlyList<string> Tags
    {
        get => _tags;
        private set
        {
            if (SetProperty(ref _tags, value))
            {
                RaisePropertyChanged(nameof(TagSummary));
                RaisePropertyChanged(nameof(HasTags));
            }
        }
    }

    public bool HasTags => Tags.Count > 0;

    public string TagSummary => string.Join(", ", Tags);

    public string AnnotationState
    {
        get => _annotationState;
        internal set => SetProperty(ref _annotationState, value ?? string.Empty);
    }

    public bool IsAnnotationsSaving
    {
        get => _isAnnotationsSaving;
        internal set => SetProperty(ref _isAnnotationsSaving, value);
    }

    internal MediaAnnotationsSnapshot? AnnotationSnapshot => _annotationSnapshot;

    internal void ApplyAnnotations(MediaAnnotations annotations, string state, MediaAnnotationsSnapshot? snapshot = null)
    {
        Rating = annotations.Rating;
        Tags = annotations.Tags;
        _annotationSnapshot = snapshot;
        AnnotationState = state;
    }

    public int CompareTo(MediaPreviewViewModel? other)
    {
        if (other == null)
        {
            return 1;
        }

        return string.CompareOrdinal(Url, other.Url);
    }

    public async Task LoadPreviewAsync(CancellationToken token)
    {
        await _previewLoadGate.WaitAsync(token);
        Bitmap? decodedBitmap = null;
        try
        {
            if (PreviewBitmap != null)
            {
                return;
            }

            IsLoading = true;
            await PreviewDecodeGate.WaitAsync(token);
            try
            {
                await using var imageStream = await _imageService.GetJpegImageStreamAsync(
                    _mapper.Map<MediaPreview>(this),
                    MediaPreviewSize.Medium,
                    token);
                decodedBitmap = await Task.Run(
                    () => Bitmap.DecodeToWidth(imageStream, PreviewDecodeWidth, BitmapInterpolationMode.MediumQuality),
                    token);
                token.ThrowIfCancellationRequested();
                PreviewBitmap = decodedBitmap;
                decodedBitmap = null;
            }
            finally
            {
                PreviewDecodeGate.Release();
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when the item leaves the viewport or the folder changes.
        }
        catch (Exception exception)
        {
            _logger.Error(exception, $"Unexpected exception during creating bitmap preview for file {Url}");
        }
        finally
        {
            decodedBitmap?.Dispose();
            IsLoading = false;
            _previewLoadGate.Release();
        }
    }

    internal async Task RemoveImagePreviewAsync()
    {
        var fileName = Path.GetFileName(Url);
        try
        {
            IsRemoving = true;
            _notificationService.SendNotification(
                new Notification(_localization.Get("Delete_Removing", fileName), string.Empty));

            var result = await _fileOperationsService.DeleteImagePreviewAsync(_mapper.Map<MediaPreview>(this));

            switch (result)
            {
                case OperationResult.Success:
                    _notificationService.SendNotification(
                        new SuccessNotification(
                            _localization.Get("Delete_Removed", fileName),
                            _fileOperationsService.CanUndoDelete
                                ? _localization["Delete_UndoHint"]
                                : string.Empty));
                    break;
                case OperationResult.Failed:
                    _notificationService.SendNotification(
                        new ErrorNotification(_localization.Get("Delete_Failed", fileName), string.Empty));
                    break;
            }
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Unexpected exception during preview deletion for file {Url}", Url);
            _notificationService.SendNotification(
                new ErrorNotification(_localization.Get("Delete_Failed", fileName), exception.Message));
        }
        finally
        {
            IsRemoving = false;
        }
    }

    private IEnumerable<FileApplicationPairViewModel> CreateOpenWithItems()
    {
        var openWithItems = new List<FileApplicationPairViewModel>();

        try
        {
            var associations = _fileAssociationsService.GetAssociations(MediaFormat);
            foreach (var association in associations)
            {
                openWithItems.Add(
                    new FileApplicationPairViewModel(
                        association,
                        _mapper.Map<MediaPreview>(this),
                        _fileOperationsService,
                        _logger));
            }
        }
        catch (Exception exception)
        {
            _logger.Error(exception, $"Error of creation open with menu for {Url}");
        }

        UseOpenWith = openWithItems.Count != 0;

        return openWithItems;
    }
}
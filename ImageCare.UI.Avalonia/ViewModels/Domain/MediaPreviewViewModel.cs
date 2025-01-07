using System;
using System.Threading.Tasks;
using System.Windows.Input;

using AutoMapper;

using Avalonia.Media.Imaging;

using CommunityToolkit.Mvvm.Input;

using ImageCare.Core.Domain;
using ImageCare.Core.Domain.Media;
using ImageCare.Core.Domain.Media.Metadata;
using ImageCare.Core.Domain.MediaFormats;
using ImageCare.Core.Services.FileOperationsService;
using ImageCare.Core.Services.FileSystemImageService;
using ImageCare.Core.Services.NotificationService;
using ImageCare.Mvvm;

using Serilog;

namespace ImageCare.UI.Avalonia.ViewModels.Domain;

internal class MediaPreviewViewModel : ViewModelBase, IComparable<MediaPreviewViewModel>
{
    private readonly IFileSystemImageService _imageService;
    private readonly IFileOperationsService _fileOperationsService;
    private readonly INotificationService _notificationService;
    private readonly IMapper _mapper;
    private readonly ILogger _logger;
    private Bitmap? _previewBitmap;
    private bool _selected;
    private bool _isLoading;
    private int _maxImageHeight = 200;
    private string _metadataString;
    private string _dateTimeString;
    private double _rotateAngle;

    public MediaPreviewViewModel(string? title,
                                 string url,
                                 MediaFormat mediaFormat,
                                 int maxImageHeight,
                                 IFileSystemImageService imageService,
                                 IFileOperationsService fileOperationsService,
                                 INotificationService notificationService,
                                 IMapper mapper,
                                 ILogger logger)
    {
        Title = title;
        Url = url;
        MaxImageHeight = maxImageHeight;
        MediaFormat = mediaFormat;

        _imageService = imageService;
        _fileOperationsService = fileOperationsService;
        _notificationService = notificationService;
        _mapper = mapper;
        _logger = logger;

        RemoveImagePreviewCommand = new AsyncRelayCommand(RemoveImagePreviewAsync);

        _ = LoadPreviewAsync();
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
        set => SetProperty(ref _previewBitmap, value);
    }

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

    public IMediaMetadata Metadata { get; internal set; }

    public int CompareTo(MediaPreviewViewModel? other)
    {
        if (other == null)
        {
            return 1;
        }

        return string.CompareOrdinal(Url, other.Url);
    }

    private async Task LoadPreviewAsync()
    {
        IsLoading = true;

        try
        {
            await using (var imageStream = await _imageService.GetJpegImageStreamAsync(_mapper.Map<MediaPreview>(this), MediaPreviewSize.Medium))
            {
                PreviewBitmap = await Task.Run(() => Bitmap.DecodeToHeight(imageStream, MaxImageHeight, BitmapInterpolationMode.LowQuality));
            }
        }
        catch (Exception exception)
        {
            _logger.Error(exception, $"Unexpected exception during creating bitmap preview for file {Url}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    internal async Task RemoveImagePreviewAsync()
    {
        try
        {
            var notificationTitle = $"Delete {Url}";
            _notificationService.SendNotification(new Notification(notificationTitle, string.Empty));

            var result = await _fileOperationsService.DeleteImagePreviewAsync(_mapper.Map<MediaPreview>(this));

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
        catch (Exception exception)
        {
            _logger.Error(exception, $"Unexpected exception during preview deletion for file {Url}");
        }
    }
}
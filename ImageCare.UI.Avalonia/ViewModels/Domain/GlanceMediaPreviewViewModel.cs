using AutoMapper;

using ImageCare.Core.Domain.MediaFormats;
using ImageCare.Core.Services.FileAssociationsService;
using ImageCare.Core.Services.MediaPreviewOperationsService;
using ImageCare.Core.Services.MediaPreviewService;
using ImageCare.Core.Services.NotificationService;

using Serilog;

namespace ImageCare.UI.Avalonia.ViewModels.Domain;

internal class GlanceMediaPreviewViewModel : MediaPreviewViewModel
{
    public GlanceMediaPreviewViewModel(string? title,
                                       string url,
                                       MediaFormat mediaFormat,
                                       int maxImageHeight,
                                       IMediaPreviewService imageService,
                                       IMediaPreviewOperationsService fileOperationsService,
                                       INotificationService notificationService,
                                       IFileAssociationsService fileAssociationsService,
                                       IMapper mapper,
                                       ILogger logger)
        : base(title, url, mediaFormat, maxImageHeight, imageService, fileOperationsService, notificationService, fileAssociationsService, mapper, logger) { }
}
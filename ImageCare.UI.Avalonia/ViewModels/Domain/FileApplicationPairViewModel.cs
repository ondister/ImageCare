using System;
using System.Windows.Input;

using ImageCare.Core.Domain;
using ImageCare.Core.Domain.Preview;
using ImageCare.Core.Services.MediaPreviewOperationsService;

using Prism.Commands;

using Serilog;

namespace ImageCare.UI.Avalonia.ViewModels.Domain;

internal sealed class FileApplicationPairViewModel
{
    private readonly FileApplicationInfo _application;
    private readonly MediaPreview _mediaPreview;
    private readonly IMediaPreviewOperationsService _fileOperationsService;
    private readonly ILogger _logger;

    public FileApplicationPairViewModel(
        FileApplicationInfo application,
        MediaPreview mediaPreview,
        IMediaPreviewOperationsService fileOperationsService,
        ILogger logger)
    {
        _application = application;
        Name = application.Name;
        _mediaPreview = mediaPreview;
        _fileOperationsService = fileOperationsService;
        _logger = logger;

        OpenWithCommand = new DelegateCommand(OpenWith, () => true);
    }

    public ICommand OpenWithCommand { get; }

    public string Name { get; }

    private void OpenWith()
    {
        try
        {
            _fileOperationsService.OpenInExternalProcess(_mediaPreview, _application);
        }
        catch (Exception exception)
        {
            _logger.Error(
                exception,
                "Error opening media preview {MediaPath} with {ApplicationName}",
                _mediaPreview.Url,
                _application.Name);
        }
    }
}

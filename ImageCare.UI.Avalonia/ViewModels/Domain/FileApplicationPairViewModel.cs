using System;
using System.Windows.Input;

using ImageCare.Core.Domain.Preview;
using ImageCare.Core.Services.FileOperationsService;

using Prism.Commands;

using Serilog;

namespace ImageCare.UI.Avalonia.ViewModels.Domain;

internal sealed class FileApplicationPairViewModel
{
    private readonly string _executablePath;
    private readonly MediaPreview _mediaPreview;
    private readonly IFileOperationsService _fileOperationsService;
    private readonly ILogger _logger;

    public FileApplicationPairViewModel(string name,
                                        string executablePath,
                                        MediaPreview mediaPreview,
                                        IFileOperationsService fileOperationsService,
                                        ILogger logger)
    {
        Name = name;
        _executablePath = executablePath;
        _mediaPreview = mediaPreview;
        _fileOperationsService = fileOperationsService;
        _logger = logger;

        OpenWithCommand = new DelegateCommand(OpenWith);
    }

    public ICommand OpenWithCommand { get; }

    public string Name { get; }

    private void OpenWith()
    {
        try
        {
            _fileOperationsService.OpenInExternalProcess(_mediaPreview, _executablePath);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, $"Error of opening media preview {_mediaPreview.Url} in external app: {_executablePath}");
        }
    }
}
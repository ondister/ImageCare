using ImageCare.Core.Domain;
using ImageCare.Core.Domain.Configuration;
using ImageCare.Core.Domain.Preview;
using ImageCare.Core.Exceptions;
using ImageCare.Core.Services.FileSystemService;
using ImageCare.Core.Services.ProcessService;

namespace ImageCare.Core.Services.MediaPreviewOperationsService;

internal sealed class ExternalProcessLauncher
{
    private readonly IFileSystemService _fileSystemService;
    private readonly IProcessService _processService;

    public ExternalProcessLauncher(
        IFileSystemService fileSystemService,
        IProcessService processService)
    {
        _fileSystemService = fileSystemService ?? throw new ArgumentNullException(nameof(fileSystemService));
        _processService = processService ?? throw new ArgumentNullException(nameof(processService));
    }

    public void Open(MediaPreview mediaPreview, string pathToExecutable)
    {
        try
        {
            if (!_fileSystemService.FileExists(pathToExecutable))
            {
                throw new ServiceException($"Executable not found: {pathToExecutable}");
            }

            if (!_fileSystemService.FileExists(mediaPreview.Url))
            {
                throw new ServiceException($"Media file not found: {mediaPreview.Url}");
            }

            _processService.StartProcess(pathToExecutable, $"\"{mediaPreview.Url}\"");
        }
        catch (ServiceException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new ServiceException("Failed to open in external process", ex);
        }
    }

    public void Open(MediaPreview mediaPreview, FileApplicationInfo application)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(application);

            if (!_fileSystemService.FileExists(mediaPreview.Url))
            {
                throw new ServiceException($"Media file not found: {mediaPreview.Url}");
            }

            switch (application.LaunchMode)
            {
                case ApplicationLaunchMode.Executable when
                    !_fileSystemService.FileExists(application.ApplicationPath):
                    throw new ServiceException($"Executable not found: {application.ApplicationPath}");
                case ApplicationLaunchMode.MacApplication when
                    !_fileSystemService.DirectoryExists(application.ApplicationPath):
                    throw new ServiceException($"Application not found: {application.ApplicationPath}");
            }

            _processService.OpenFile(
                mediaPreview.Url,
                application.LaunchMode,
                application.ApplicationPath);
        }
        catch (ServiceException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new ServiceException("Failed to open in external application", ex);
        }
    }
}

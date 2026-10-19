using ImageCare.Core.Domain.Configuration;

namespace ImageCare.Core.Services.ProcessService;

public interface IProcessService
{
    void StartProcess(string fileName, string arguments);

    void OpenFile(string filePath, ApplicationLaunchMode launchMode, string applicationPath);
}

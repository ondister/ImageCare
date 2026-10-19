using System.Diagnostics;

using ImageCare.Core.Domain.Configuration;

namespace ImageCare.Core.Services.ProcessService;

public sealed class SystemProcessService : IProcessService
{
    public void StartProcess(string fileName, string arguments)
    {
        Process.Start(
            new ProcessStartInfo
            {
                UseShellExecute = true,
                FileName = fileName,
                Arguments = arguments
            });
    }

    public void OpenFile(string filePath, ApplicationLaunchMode launchMode, string applicationPath)
    {
        if (launchMode == ApplicationLaunchMode.Executable)
        {
            StartApplication(applicationPath, filePath);
            return;
        }

        var processInfo = new ProcessStartInfo
        {
            UseShellExecute = false,
            FileName = "/usr/bin/open"
        };

        switch (launchMode)
        {
            case ApplicationLaunchMode.FileManager:
                processInfo.ArgumentList.Add("-R");
                break;
            case ApplicationLaunchMode.MacApplication:
                processInfo.ArgumentList.Add("-a");
                processInfo.ArgumentList.Add(applicationPath);
                break;
            case ApplicationLaunchMode.SystemDefault:
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(launchMode), launchMode, null);
        }

        processInfo.ArgumentList.Add(filePath);
        Process.Start(processInfo);
    }

    private static void StartApplication(string applicationPath, string filePath)
    {
        var processInfo = new ProcessStartInfo
        {
            UseShellExecute = false,
            FileName = applicationPath
        };
        processInfo.ArgumentList.Add(filePath);
        Process.Start(processInfo);
    }
}

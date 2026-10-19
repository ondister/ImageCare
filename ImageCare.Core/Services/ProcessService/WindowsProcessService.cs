using System.Diagnostics;

using ImageCare.Core.Domain.Configuration;

namespace ImageCare.Core.Services.ProcessService;

public sealed class WindowsProcessService : IProcessService
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
        switch (launchMode)
        {
            case ApplicationLaunchMode.SystemDefault:
                Process.Start(
                    new ProcessStartInfo
                    {
                        UseShellExecute = true,
                        FileName = filePath
                    });
                return;
            case ApplicationLaunchMode.FileManager:
                Process.Start(
                    new ProcessStartInfo
                    {
                        UseShellExecute = true,
                        FileName = "explorer.exe",
                        Arguments = $"/select,\"{filePath}\""
                    });
                return;
            case ApplicationLaunchMode.Executable:
            case ApplicationLaunchMode.MacApplication:
                var processInfo = new ProcessStartInfo
                {
                    UseShellExecute = true,
                    FileName = applicationPath
                };
                processInfo.ArgumentList.Add(filePath);
                Process.Start(processInfo);
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(launchMode), launchMode, null);
        }
    }
}

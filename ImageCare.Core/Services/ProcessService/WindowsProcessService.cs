using System.Diagnostics;

namespace ImageCare.Core.Services.ProcessService;

public sealed class WindowsProcessService : IProcessService
{
	public void StartProcess(string fileName, string arguments)
	{
		var processInfo = new ProcessStartInfo
		{
			UseShellExecute = true,
			FileName = fileName,
			Arguments = arguments
		};

		Process.Start(processInfo);
	}
}
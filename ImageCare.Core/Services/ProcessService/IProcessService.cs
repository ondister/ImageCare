namespace ImageCare.Core.Services.ProcessService;

public interface IProcessService
{
	void StartProcess(string fileName, string arguments);
}
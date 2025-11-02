namespace ImageCare.Core.Services.ConfigurationService;

public interface IConfigurationFileSource
{
	string GetConfigurationFilePath();

	void EnsureDirectoryExists();

	bool ConfigurationFileExists();
}
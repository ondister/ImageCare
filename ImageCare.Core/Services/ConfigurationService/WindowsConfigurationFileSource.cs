namespace ImageCare.Core.Services.ConfigurationService;

public sealed class WindowsConfigurationFileSource : IConfigurationFileSource
{
	private const string ConfigurationFilename = "configuration.json";
	private const string AppName = "ImageCare";

	private readonly string _configurationDirectoryPath;
	private readonly string _configurationFilePath;

	public WindowsConfigurationFileSource()
	{
		var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
		_configurationDirectoryPath = Path.Combine(appDataPath, AppName);
		_configurationFilePath = Path.Combine(_configurationDirectoryPath, ConfigurationFilename);
	}

	public string GetConfigurationFilePath()
	{
		return _configurationFilePath;
	}

	public void EnsureDirectoryExists()
	{
		if (!Directory.Exists(_configurationDirectoryPath))
		{
			Directory.CreateDirectory(_configurationDirectoryPath);
		}
	}

	public bool ConfigurationFileExists()
	{
		return File.Exists(_configurationFilePath);
	}
}
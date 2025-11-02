using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text.Json;

using ImageCare.Core.Exceptions;

namespace ImageCare.Core.Services.ConfigurationService;

public sealed class JsonConfigurationService : IConfigurationService, IDisposable
{
	private const string _configurationFilename = "configuration.json";
	private const string _exceptionMessage = "Unexpected exception in Json configuration service";

	private readonly Subject<Configuration> _configurationSavedSubject;

	public JsonConfigurationService()
	{
		_configurationSavedSubject = new Subject<Configuration>();

		CreateConfigurationFileIfNeeded();
	}

	/// <inheritdoc />
	public IObservable<Configuration> ConfigurationSaved => _configurationSavedSubject.AsObservable();

	public Lazy<Configuration> Configuration { get; } = new(LoadConfiguration);

	public void SaveConfiguration()
	{
		try
		{
			var configuration = Configuration.Value;
			var configurationPath = Path.Combine(GetConfigurationDirectoryPath(), _configurationFilename);

			using (var fileStream = new FileStream(configurationPath, FileMode.Create, FileAccess.Write, FileShare.Read))
			{
				JsonSerializer.Serialize(fileStream, configuration, new JsonSerializerOptions { WriteIndented = true });
			}

			_configurationSavedSubject.OnNext(configuration);
		}
		catch (Exception exception)
		{
			throw new ServiceException(_exceptionMessage, exception);
		}
	}

	public void Dispose()
	{
		_configurationSavedSubject.Dispose();
	}

	private static Configuration LoadConfiguration()
	{
		try
		{
			var configurationPath = Path.Combine(GetConfigurationDirectoryPath(), _configurationFilename);
			using (var fileStream = new FileStream(configurationPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
			{
				return JsonSerializer.Deserialize<Configuration>(fileStream) ?? new Configuration();
			}
		}
		catch (Exception exception)
		{
			throw new ServiceException(_exceptionMessage, exception);
		}
	}

	private static void CreateConfigurationFileIfNeeded()
	{
		var configurationFolderPath = GetConfigurationDirectoryPath();
		if (!Directory.Exists(configurationFolderPath))
		{
			Directory.CreateDirectory(configurationFolderPath);
		}

		var configurationPath = Path.Combine(configurationFolderPath, _configurationFilename);

		if (File.Exists(configurationPath))
		{
			return;
		}

		using (var fileStream = new FileStream(configurationPath, FileMode.Create, FileAccess.ReadWrite))
		{
			JsonSerializer.Serialize(fileStream, new Configuration());
		}
	}

	private static string GetConfigurationDirectoryPath()
	{
		return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ImageCare");
	}
}
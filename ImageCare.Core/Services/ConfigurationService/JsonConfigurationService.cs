using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text.Json;

using ImageCare.Core.Exceptions;

namespace ImageCare.Core.Services.ConfigurationService;

public sealed class JsonConfigurationService : IConfigurationService, IDisposable
{
	private const string ExceptionMessage = "Unexpected exception in Json configuration service";

	private readonly Subject<Configuration> _configurationSavedSubject;
	private readonly IConfigurationFileSource _fileSource;
	private readonly JsonSerializerOptions _jsonOptions;
	private bool _disposed;

	public JsonConfigurationService(IConfigurationFileSource fileSource)
	{
		_fileSource = fileSource ?? throw new ArgumentNullException(nameof(fileSource));
		_configurationSavedSubject = new Subject<Configuration>();
		Configuration = new Lazy<Configuration>(LoadConfiguration);
		_jsonOptions = new JsonSerializerOptions { WriteIndented = true };

		CreateConfigurationFileIfNeeded();
	}

	public IObservable<Configuration> ConfigurationSaved => _configurationSavedSubject.AsObservable();

	public Lazy<Configuration> Configuration { get; }

	public void SaveConfiguration()
	{
		if (_disposed)
		{
			throw new ObjectDisposedException(nameof(JsonConfigurationService));
		}

		try
		{
			var configuration = Configuration.Value;
			var filePath = _fileSource.GetConfigurationFilePath();

			_fileSource.EnsureDirectoryExists();

			using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
			JsonSerializer.Serialize(fileStream, configuration, _jsonOptions);

			_configurationSavedSubject.OnNext(configuration);
		}
		catch (Exception exception)
		{
			throw new ServiceException(ExceptionMessage, exception);
		}
	}

	public void Dispose()
	{
		if (!_disposed)
		{
			_configurationSavedSubject.Dispose();
			_disposed = true;
		}
	}

	private Configuration LoadConfiguration()
	{
		try
		{
			if (!_fileSource.ConfigurationFileExists())
			{
				return new Configuration();
			}

			var filePath = _fileSource.GetConfigurationFilePath();
			using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
			return JsonSerializer.Deserialize<Configuration>(fileStream) ?? new Configuration();
		}
		catch (Exception exception)
		{
			throw new ServiceException(ExceptionMessage, exception);
		}
	}

	private void CreateConfigurationFileIfNeeded()
	{
		if (_fileSource.ConfigurationFileExists())
		{
			return;
		}

		_fileSource.EnsureDirectoryExists();

		var filePath = _fileSource.GetConfigurationFilePath();
		using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.ReadWrite);
		JsonSerializer.Serialize(fileStream, new Configuration(), _jsonOptions);
	}
}
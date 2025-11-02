using System.Collections.Concurrent;
using System.Reactive.Disposables;

using ImageCare.Core.Domain;
using ImageCare.Core.Domain.MediaFormats;
using ImageCare.Core.Services.ConfigurationService;

namespace ImageCare.Core.Services.FileAssociationsService;

public sealed class ConfigurationFileAssociationsService : IFileAssociationsService, IDisposable
{
	private readonly IConfigurationService _configurationService;
	private readonly ConcurrentDictionary<MediaFormat, IEnumerable<FileApplicationInfo>> _associations;

	private readonly CompositeDisposable _compositeDisposable;

	public ConfigurationFileAssociationsService(IConfigurationService configurationService)
	{
		_configurationService = configurationService;
		_associations = new ConcurrentDictionary<MediaFormat, IEnumerable<FileApplicationInfo>>();

		_compositeDisposable = new CompositeDisposable
		{
			configurationService.ConfigurationSaved.Subscribe(OnConfigurationSaved)
		};
	}

	/// <inheritdoc />
	public void Dispose()
	{
		_compositeDisposable.Dispose();
	}

	/// <inheritdoc />
	public IEnumerable<FileApplicationInfo> GetAssociations(MediaFormat mediaFormat)
	{
		if (!_associations.ContainsKey(mediaFormat))
		{
			_associations.TryAdd(mediaFormat, AddAssociations(mediaFormat));
		}

		return _associations[mediaFormat];
	}

	private void OnConfigurationSaved(Configuration configuration)
	{
		// Just clear associations. All of them will be reloaded on demand in the GetAssociations method
		_associations.Clear();
	}

	private IEnumerable<FileApplicationInfo> AddAssociations(MediaFormat mediaFormat)
	{
		var associationsList = new List<FileApplicationInfo>();

		var pairs = _configurationService.Configuration.Value.ApplicationAssociationPairs;

		foreach (var fileExtension in mediaFormat.FileExtensions)
		{
			foreach (var pair in pairs.Where(a => a.FileExtension.Equals(fileExtension, StringComparison.OrdinalIgnoreCase)))
			{
				if (File.Exists(pair.ApplicationPath))
				{
					associationsList.Add(new FileApplicationInfo(pair.Name, pair.ApplicationPath));
				}
			}
		}

		return associationsList.DistinctBy(a => a.Name);
	}
}
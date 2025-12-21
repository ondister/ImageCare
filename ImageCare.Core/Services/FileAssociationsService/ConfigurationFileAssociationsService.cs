using System.Collections.Concurrent;
using System.Reactive.Disposables;

using ImageCare.Core.Domain;
using ImageCare.Core.Domain.Configuration;
using ImageCare.Core.Domain.MediaFormats;
using ImageCare.Core.Services.ConfigurationService;

namespace ImageCare.Core.Services.FileAssociationsService;

public sealed class ConfigurationFileAssociationsService : IFileAssociationsService, IDisposable
{
	private readonly IConfigurationService _configurationService;

	private readonly ConcurrentDictionary<MediaFormat, IEnumerable<FileApplicationInfo>> _associations;
	private readonly CompositeDisposable _compositeDisposable;
	private bool _disposed;

	public ConfigurationFileAssociationsService(IConfigurationService configurationService)
	{
		_configurationService = configurationService;
		_associations = new ConcurrentDictionary<MediaFormat, IEnumerable<FileApplicationInfo>>();

		_compositeDisposable = new CompositeDisposable
		{
			configurationService.ConfigurationSaved.Subscribe(OnConfigurationSaved)
		};
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_compositeDisposable.Dispose();
		_disposed = true;
	}

	public IEnumerable<FileApplicationInfo> GetAssociations(MediaFormat mediaFormat)
	{
		if (_disposed)
		{
			throw new ObjectDisposedException(nameof(ConfigurationFileAssociationsService));
		}

		return _associations.GetOrAdd(mediaFormat, AddAssociations);
	}

	private void OnConfigurationSaved(Configuration configuration)
	{
		_associations.Clear();
	}

	private IEnumerable<FileApplicationInfo> AddAssociations(MediaFormat mediaFormat)
	{
		var associationsSet = new HashSet<FileApplicationInfo>(FileApplicationInfoComparer.Instance);
		var pairs = _configurationService.Configuration.Value.ApplicationAssociationPairs;

		foreach (var fileExtension in mediaFormat.FileExtensions)
		{
			foreach (var pair in pairs.Where(a =>
				                                 a.FileExtension.Equals(fileExtension, StringComparison.OrdinalIgnoreCase)))
			{
				associationsSet.Add(new FileApplicationInfo(pair.Name, pair.ApplicationPath));
			}
		}

		return associationsSet;
	}
}
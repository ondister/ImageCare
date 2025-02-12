using System.Collections.Concurrent;

using ImageCare.Core.Domain;
using ImageCare.Core.Domain.MediaFormats;
using ImageCare.Core.Services.ConfigurationService;

namespace ImageCare.Core.Services.FileAssociationsService;

public sealed class ConfigurationFileAssociationsService : IFileAssociationsService
{
    private readonly IConfigurationService _configurationService;
    private readonly ConcurrentDictionary<MediaFormat, IEnumerable<FileApplicationInfo>> _associations;

    public ConfigurationFileAssociationsService(IConfigurationService configurationService)
    {
        _configurationService = configurationService;
        _associations = new ConcurrentDictionary<MediaFormat, IEnumerable<FileApplicationInfo>>();
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
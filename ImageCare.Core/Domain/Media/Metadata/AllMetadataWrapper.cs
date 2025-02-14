using System.Collections.Concurrent;
using System.Collections.ObjectModel;

namespace ImageCare.Core.Domain.Media.Metadata;

public abstract class AllMetadataWrapper
{
    private readonly ConcurrentDictionary<string, string> _allMetadata;

    protected AllMetadataWrapper()
    {
        _allMetadata = new ConcurrentDictionary<string, string>();
        AllMetadata = new ReadOnlyDictionary<string, string>(_allMetadata);
    }

    public IReadOnlyDictionary<string, string> AllMetadata { get; }

    internal void AddOrUpdateMetadata(string key, string value)
    {
        _allMetadata.AddOrUpdate(key, value, (_, _) => value);
    }
}
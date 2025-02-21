using System.Collections.Concurrent;
using System.Collections.ObjectModel;

using ImageCare.Core.Domain.MediaFormats;

namespace ImageCare.Core.Domain.Folders;

public sealed class FolderStatistics
{
	private readonly ConcurrentDictionary<MediaFormat, long> _mediaCount;

	public FolderStatistics()
	{
		_mediaCount = new ConcurrentDictionary<MediaFormat, long>();
		MediaCount = new ReadOnlyDictionary<MediaFormat, long>(_mediaCount);
	}

	public IReadOnlyDictionary<MediaFormat, long> MediaCount { get; }

	internal void AddMediaFormatStatistics(MediaFormat mediaFormat, long filesCount)
	{
		_mediaCount.AddOrUpdate(mediaFormat, filesCount, (key, value) => value);
	}

	/// <inheritdoc />
	public override string ToString()
	{
		return MediaCount.Where(kvp => !kvp.Key.Equals(MediaFormat.MediaFormatUnknown)).Sum(v => v.Value).ToString();
	}
}
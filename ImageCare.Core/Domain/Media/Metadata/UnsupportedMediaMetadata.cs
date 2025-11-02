namespace ImageCare.Core.Domain.Media.Metadata;

public sealed class UnsupportedMediaMetadata : AllMetadataWrapper, IMediaMetadata
{
	public UnsupportedMediaMetadata(DateTime creationDateTime)
	{
		CreationDateTime = creationDateTime;
	}

	/// <inheritdoc />
	public int Width { get; } = 0;

	/// <inheritdoc />
	public int Height { get; } = 0;

	/// <inheritdoc />
	public DateTime CreationDateTime { get; }

	/// <inheritdoc />
	public ExifOrientation Orientation { get; set; }

	/// <inheritdoc />
	public Location Location { get; } = Location.Empty;

	/// <inheritdoc />
	public string GetString()
	{
		return "-";
	}
}
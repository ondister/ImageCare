﻿namespace ImageCare.Core.Domain.Media.Metadata;

public class VideoMediaMetadata : AllMetadataWrapper, IVideoMetadata
{
	public VideoMediaMetadata(DateTime creationDateTime, int width, int height)
	{
		CreationDateTime = creationDateTime;
		Width = width;
		Height = height;
	}

	/// <inheritdoc />
	public int Width { get; }

	/// <inheritdoc />
	public int Height { get; }

	/// <inheritdoc />
	public DateTime CreationDateTime { get; }

	/// <inheritdoc />
	public ExifOrientation Orientation { get; set; }

	/// <inheritdoc />
	public TimeSpan Duration { get; set; }

	/// <inheritdoc />
	public Location Location { get; } = Location.Empty;

	/// <inheritdoc />
	public string GetString()
	{
		return $"{Width}*{Height} - {Duration.ToString("g")}";
	}
}
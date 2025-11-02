using System.Runtime.CompilerServices;

using ImageCare.Core.Domain.Media.Metadata;
using ImageCare.Core.Domain.Preview;

[assembly: InternalsVisibleTo("ImageCare.Core.Tests")]

namespace ImageCare.Core.Domain.Media;

internal interface IMediaPreviewProvider
{
	IMediaMetadata GetMediaMetadata(string url);

	DateTime? GetCreationDateTime(string url);

	Stream GetPreviewJpegStream(string url, MediaPreviewSize size);
}
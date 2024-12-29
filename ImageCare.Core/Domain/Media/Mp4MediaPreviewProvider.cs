using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ImageCare.Core.Domain.Media
{
    internal sealed class Mp4MediaPreviewProvider:IMediaPreviewProvider
    {
        /// <inheritdoc />
        public Stream GetPreviewJpegStream(string url, ImagePreviewSize size)
        {
            string _unsupportedMediaPreview = @"Domain\Media\Assets\unknown_media_preview.jpg";
            return File.OpenRead(_unsupportedMediaPreview);
        }
    }
}

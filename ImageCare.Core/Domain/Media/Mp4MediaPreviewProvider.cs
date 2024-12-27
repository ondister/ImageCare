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
            throw new NotImplementedException();
        }
    }
}

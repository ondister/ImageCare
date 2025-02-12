using ImageCare.Core.Domain;
using ImageCare.Core.Domain.MediaFormats;

namespace ImageCare.Core.Services.FileAssociationsService;

public interface IFileAssociationsService
{
    IEnumerable<FileApplicationInfo> GetAssociations(MediaFormat mediaFormat);
}
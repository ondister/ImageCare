using ImageCare.Core.Domain;

namespace ImageCare.Core.Services;

public interface IFolderService
{
    public IObservable<SelectedDirectory> FileSystemItemSelected { get; }

    Task<DirectoryModel> GetDirectoryModelAsync(DirectoryModel? directoryModel = null);

    Task<DirectoryModel> GetDirectoryModelAsync(string directoryPath);

    Task<IEnumerable<FileModel>> GetFileModelAsync(DirectoryModel directoryModel, string searchPattern);

    Task<IEnumerable<FileModel>> GetFileModelAsync(string directoryPath, string searchPattern);

    void SetSelectedDirectory(SelectedDirectory selecteddirectory);
}
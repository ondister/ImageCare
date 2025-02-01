using ImageCare.Core.Domain;

namespace ImageCare.Core.Services.FolderService;

public interface IFolderService
{
    public IObservable<SelectedDirectory> FileSystemItemSelected { get; }

    public IObservable<SelectedDirectory> FolderVisited { get; }

    public IObservable<SelectedDirectory> FolderLeft { get; }

    Task<DirectoryModel> GetDirectoryModelAsync(DirectoryModel? directoryModel = null);

    Task<DirectoryModel> GetDirectoryModelAsync(string directoryPath);

    Task<IEnumerable<FileModel>> GetFileModelAsync(DirectoryModel directoryModel, string searchPattern);

    Task<IEnumerable<FileModel>> GetFileModelAsync(string directoryPath, string searchPattern);

    Task<DirectoryModel> GetCustomDirectoriesLevelAsync(DirectoryModel directoryModel, bool preview = false);

    void SetSelectedDirectory(SelectedDirectory selecteddirectory);

    DirectoryModel? GetSelectedDirectory(FileManagerPanel fileManagerPanel);

    void AddVisitingFolder(DirectoryModel directoryModel, FileManagerPanel fileManagerPanel);

    void RemoveVisitingFolder(DirectoryModel directoryModel, FileManagerPanel fileManagerPanel);

    void RemoveFolder(DirectoryModel directoryModel);

    DirectoryModel? CreateSubFolder(DirectoryModel directoryModel);

    string? RenameFolder(string? newName, string path);
}
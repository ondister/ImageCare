using ImageCare.Core.Domain.Folders;

namespace ImageCare.Core.Services.FolderHistoryService;

public interface IFolderHistoryService
{
    event EventHandler? HistoryChanged;

    void AddFolderToHistory(string folderPath);

    void AddFolderToPopular(string folderPath);

    void RemoveFolderFromPopular(string folderPath);

    IReadOnlyList<string> GetPopularFolders();

    IReadOnlyList<string> GetRecentFolders();

    IReadOnlyList<SmartDirectoryModel> GetSmartFolders(int minIntersections = 3);

    void SaveHistory();

    void LoadHistory();
}
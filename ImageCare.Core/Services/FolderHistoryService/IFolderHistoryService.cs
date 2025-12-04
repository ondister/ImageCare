using ImageCare.Core.Domain.Folders;

namespace ImageCare.Core.Services.FolderHistoryService;

public interface IFolderHistoryService
{
    void AddFolderToHistory(string folderPath);

    IReadOnlyList<string> GetRecentFolders();

    IReadOnlyList<SmartDirectoryModel> GetSmartFolders(int minIntersections = 3);

    void SaveHistory();

    void LoadHistory();
}
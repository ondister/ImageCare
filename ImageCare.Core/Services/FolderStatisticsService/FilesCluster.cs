using ImageCare.Core.Domain.Folders;

namespace ImageCare.Core.Services.FolderStatisticsService;

public class FilesCluster
{
    private readonly Dictionary<string, FileModel> _files;

    public FilesCluster(int id)
    {
        Id = id;
        _files = new Dictionary<string, FileModel>();
    }

    public FilesCluster(int id, IEnumerable<FileModel> files)
    {
        Id = id;
        _files = files?.ToDictionary(GetFileKey, f => f)
              ?? new Dictionary<string, FileModel>();
    }

    public int Id { get; }

    public string ColorCode { get; set; } = "#FFFFFF";

    public IReadOnlyDictionary<string, FileModel> Files => _files;

    public DateTime StartTime =>
        _files.Values
              .OrderBy(f => f.CreatedDateTime)
              .FirstOrDefault()
              ?.CreatedDateTime
     ?? DateTime.MinValue;

    public DateTime EndTime =>
        _files.Values
              .OrderBy(f => f.CreatedDateTime)
              .LastOrDefault()
              ?.CreatedDateTime
     ?? DateTime.MinValue;

    public TimeSpan Duration => EndTime - StartTime;

    public int FileCount => _files.Count;

    public double Density => FileCount / Math.Max(Duration.TotalMinutes, 1);

    public bool ContainsFile(string fileKey)
    {
        return _files.ContainsKey(fileKey);
    }

    private string GetFileKey(FileModel file)
    {
        return file.FullName;
    }
}
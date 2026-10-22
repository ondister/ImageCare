using ImageCare.Core.Domain.Folders;

namespace ImageCare.Core.Services.FileSystemService;

public interface IFileSystemService
{
    bool FileExists(string path);

    bool DirectoryExists(string path);

    bool IsDirectory(string path);

    FileInfo GetFileInfo(string path);

    DirectoryInfo GetDirectoryInfo(string path);

    Stream OpenRead(string path);

    Stream Create(string path);

    void MoveFile(string source, string destination);

    void CopyFile(string source, string destination);

    void DeleteFile(string path);

    void MoveDirectory(string source, string destination);

    void DeleteDirectory(string path, bool recursive);

    void CreateDirectory(string path);

    string[] GetFiles(string directory);

    string[] GetFiles(string directory, string searchPattern);

    IEnumerable<string> EnumerateFiles(string directory, string searchPattern);

    IEnumerable<FileModel> EnumerateFiles(string directory, string searchPattern, SearchOption searchOption);

    string[] GetDirectories(string directory);

    IEnumerable<string> EnumerateDirectories(string directory, string searchPattern, SearchOption searchOption = SearchOption.TopDirectoryOnly);

    void CopyFileMetadata(string source, string destination);

    /// <summary>
    /// Copies only timestamps and attributes from <paramref name="source"/> to
    /// <paramref name="destination"/>. Unlike <see cref="CopyFileMetadata"/> this does not
    /// touch security descriptors or alternate data streams and is safe to use for transient
    /// files (for example a temporary rewrite target in the same directory) where the
    /// destination must not inherit a security descriptor it is not allowed to apply.
    /// </summary>
    void CopyFileTimestampsAndAttributes(string source, string destination);

    void CopyDirectoryMetadata(string source, string destination);

    void SafeDelete(string path);

    void SafeDeleteDirectory(string path);

    string GetFileExtension(string path);

    string? RenameFolder(string? newName, string path);
}
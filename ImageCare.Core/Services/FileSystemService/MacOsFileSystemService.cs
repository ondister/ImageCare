using System.ComponentModel;
using System.Runtime.InteropServices;

using ImageCare.Core.Domain.Folders;
using ImageCare.Core.Exceptions;

namespace ImageCare.Core.Services.FileSystemService;

public sealed partial class MacOsFileSystemService : IFileSystemService
{
    private const uint CopyFileMetadataFlags = 0x00000007;

    [LibraryImport("/usr/lib/libSystem.B.dylib", EntryPoint = "copyfile", SetLastError = true,
        StringMarshalling = StringMarshalling.Utf8)]
    private static partial int CopyFileNative(string source, string destination, IntPtr state, uint flags);
    public bool FileExists(string path) => File.Exists(path);

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public bool IsDirectory(string path)
    {
        if (DirectoryExists(path))
        {
            return true;
        }

        return FileExists(path) ? false : throw new FileNotFoundException("Path not found", path);
    }

    public FileInfo GetFileInfo(string path) => new(path);

    public DirectoryInfo GetDirectoryInfo(string path) => new(path);

    public Stream OpenRead(string path) =>
        new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);

    public Stream Create(string path) =>
        new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);

    public void MoveFile(string source, string destination) => File.Move(source, destination);

    public void CopyFile(string source, string destination) => File.Copy(source, destination);

    public void DeleteFile(string path) => File.Delete(path);

    public void MoveDirectory(string source, string destination) => Directory.Move(source, destination);

    public void DeleteDirectory(string path, bool recursive) => Directory.Delete(path, recursive);

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public string[] GetFiles(string directory) => Directory.GetFiles(directory);

    public string[] GetFiles(string directory, string searchPattern) => Directory.GetFiles(directory, searchPattern);

    public IEnumerable<string> EnumerateFiles(string directory, string searchPattern) =>
        Directory.EnumerateFiles(directory, searchPattern, SearchOption.TopDirectoryOnly);

    public IEnumerable<FileModel> EnumerateFiles(
        string directory,
        string searchPattern,
        SearchOption searchOption) =>
        Directory.EnumerateFiles(directory, searchPattern, searchOption)
                 .Select(path =>
                 {
                     var fileInfo = new FileInfo(path);
                     return new FileModel(fileInfo.Name, fileInfo.FullName, fileInfo.LastWriteTime);
                 });

    public string[] GetDirectories(string directory) => Directory.GetDirectories(directory);

    public IEnumerable<string> EnumerateDirectories(
        string directory,
        string searchPattern,
        SearchOption searchOption = SearchOption.TopDirectoryOnly) =>
        Directory.EnumerateDirectories(directory, searchPattern, searchOption);

    public void CopyFileMetadata(string source, string destination)
    {
        var sourceInfo = new FileInfo(source);
        sourceInfo.Refresh();
        if (!sourceInfo.Exists)
        {
            throw new FileNotFoundException("Metadata source file was not found.", source);
        }

        if (OperatingSystem.IsMacOS())
        {
            try
            {
                if (CopyFileNative(source, destination, IntPtr.Zero, CopyFileMetadataFlags) != 0)
                {
                    throw new IOException(
                        $"Failed to copy macOS metadata from '{source}' to '{destination}'.",
                        new Win32Exception(Marshal.GetLastPInvokeError()));
                }
            }
            catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
            {
                CopyManagedFileMetadata(sourceInfo, destination);
                return;
            }
        }
        else
        {
            CopyManagedFileMetadata(sourceInfo, destination);
            return;
        }

        File.SetLastWriteTimeUtc(destination, sourceInfo.LastWriteTimeUtc);
        File.SetLastAccessTimeUtc(destination, sourceInfo.LastAccessTimeUtc);
    }

    private static void CopyManagedFileMetadata(FileInfo sourceInfo, string destination)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(destination, File.GetUnixFileMode(sourceInfo.FullName));
        }

        File.SetLastWriteTimeUtc(destination, sourceInfo.LastWriteTimeUtc);
        File.SetLastAccessTimeUtc(destination, sourceInfo.LastAccessTimeUtc);
    }

    public void CopyFileTimestampsAndAttributes(string source, string destination)
    {
        var sourceInfo = new FileInfo(source);
        sourceInfo.Refresh();
        if (!sourceInfo.Exists)
        {
            throw new FileNotFoundException("Metadata source file was not found.", source);
        }

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(destination, File.GetUnixFileMode(sourceInfo.FullName));
        }

        File.SetLastWriteTimeUtc(destination, sourceInfo.LastWriteTimeUtc);
        File.SetLastAccessTimeUtc(destination, sourceInfo.LastAccessTimeUtc);
        File.SetAttributes(destination, sourceInfo.Attributes);
    }

    public void CopyDirectoryMetadata(string source, string destination)
    {
        var sourceInfo = new DirectoryInfo(source);
        Directory.SetLastWriteTimeUtc(destination, sourceInfo.LastWriteTimeUtc);
        Directory.SetLastAccessTimeUtc(destination, sourceInfo.LastAccessTimeUtc);
    }

    public void SafeDelete(string path)
    {
        if (FileExists(path))
        {
            DeleteFile(path);
        }
    }

    public void SafeDeleteDirectory(string path)
    {
        if (DirectoryExists(path))
        {
            // The MVP does not implement macOS Trash. Refuse recursive permanent deletion.
            DeleteDirectory(path, recursive: false);
        }
    }

    public string GetFileExtension(string path) => Path.GetExtension(path);

    public string? RenameFolder(string? newName, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ServiceException("Path cannot be null or empty");
        }

        try
        {
            var directoryInfo = GetDirectoryInfo(path);
            if (!directoryInfo.Exists || string.IsNullOrWhiteSpace(newName))
            {
                return directoryInfo.Name;
            }

            var parent = directoryInfo.Parent
                         ?? throw new ServiceException($"Cannot rename root directory '{path}'");
            var newPath = Path.Combine(parent.FullName, newName);
            if (DirectoryExists(newPath))
            {
                return directoryInfo.Name;
            }

            MoveDirectory(path, newPath);
            return newName;
        }
        catch (Exception ex) when (ex is not ServiceException)
        {
            throw new ServiceException($"Failed to rename folder from '{path}' to '{newName}'", ex);
        }
    }
}

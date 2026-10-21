using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.AccessControl;

using ImageCare.Core.Domain.Folders;
using ImageCare.Core.Exceptions;
using ImageCare.Core.Services.FileSystemService.Windows;

using Microsoft.VisualBasic.FileIO;

using SearchOption = System.IO.SearchOption;

namespace ImageCare.Core.Services.FileSystemService;

public sealed class WindowsFileSystemService : IFileSystemService
{
    public bool FileExists(string path)
    {
        return File.Exists(path);
    }

    public bool DirectoryExists(string path)
    {
        return Directory.Exists(path);
    }

    public bool IsDirectory(string path)
    {
        if (DirectoryExists(path))
        {
            return true;
        }

        return FileExists(path) ? false : throw new FileNotFoundException("Path not found", path);
    }

    public FileInfo GetFileInfo(string path)
    {
        return new FileInfo(path);
    }

    public DirectoryInfo GetDirectoryInfo(string path)
    {
        return new DirectoryInfo(path);
    }

    public Stream OpenRead(string path)
    {
        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
    }

    public Stream Create(string path)
    {
        return new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
    }

    public void MoveFile(string source, string destination)
    {
        File.Move(source, destination);
    }

    /// <inheritdoc />
    public void CopyFile(string source, string destination)
    {
        File.Copy(source, destination);
    }

    public void DeleteFile(string path)
    {
        File.Delete(path);
    }

    public void MoveDirectory(string source, string destination)
    {
        Directory.Move(source, destination);
    }

    public void DeleteDirectory(string path, bool recursive)
    {
        Directory.Delete(path, recursive);
    }

    public void CreateDirectory(string path)
    {
        Directory.CreateDirectory(path);
    }

    public string[] GetFiles(string directory)
    {
        return Directory.GetFiles(directory);
    }

    /// <inheritdoc />
    public string[] GetFiles(string directory, string searchPattern)
    {
        return Directory.GetFiles(directory, searchPattern);
    }

    /// <inheritdoc />
    public IEnumerable<string> EnumerateFiles(string directory, string searchPattern)
    {
        return Directory.EnumerateFiles(directory, searchPattern, SearchOption.TopDirectoryOnly);
    }

    public IEnumerable<FileModel> EnumerateFiles(string directory, string searchPattern, SearchOption searchOption)
    {
        return NativeDirectoryEnumerator.EnumerateFiles(directory, searchPattern, searchOption).Select(f => new FileModel(f.Name, f.Path, f.LastWriteTime));
    }

    public string[] GetDirectories(string directory)
    {
        return Directory.GetDirectories(directory);
    }

    /// <inheritdoc />
    public IEnumerable<string> EnumerateDirectories(string directory, string searchPattern, SearchOption searchOption = SearchOption.TopDirectoryOnly)
    {
        return Directory.EnumerateDirectories(directory, searchPattern,searchOption);
    }

    public void CopyFileMetadata(string source, string destination)
    {
        var sourceInfo = new FileInfo(source);
        sourceInfo.Refresh();
        if (!sourceInfo.Exists)
        {
            throw new FileNotFoundException("Metadata source file was not found.", source);
        }

        var attributes = sourceInfo.Attributes;
        var creationTimeUtc = sourceInfo.CreationTimeUtc;
        var lastWriteTimeUtc = sourceInfo.LastWriteTimeUtc;
        var lastAccessTimeUtc = sourceInfo.LastAccessTimeUtc;

        if (OperatingSystem.IsWindows())
        {
            CopyAlternateDataStreams(source, destination);

            var destinationInfo = new FileInfo(destination);
            const AccessControlSections requiredSections =
                AccessControlSections.Access
                | AccessControlSections.Owner
                | AccessControlSections.Group;
            var sourceRequiredSecurity = sourceInfo.GetAccessControl(requiredSections);
            var requiredSecurity = new FileSecurity();
            requiredSecurity.SetSecurityDescriptorBinaryForm(
                sourceRequiredSecurity.GetSecurityDescriptorBinaryForm(),
                requiredSections);
            destinationInfo.SetAccessControl(requiredSecurity);

            try
            {
                var sourceAuditSecurity = sourceInfo.GetAccessControl(AccessControlSections.Audit);
                var auditSecurity = new FileSecurity();
                auditSecurity.SetSecurityDescriptorBinaryForm(
                    sourceAuditSecurity.GetSecurityDescriptorBinaryForm(),
                    AccessControlSections.Audit);
                destinationInfo.SetAccessControl(auditSecurity);
            }
            catch (Exception exception) when (IsAuditMetadataUnavailable(exception))
            {
                // Reading or applying SACLs requires an elevated security privilege that is
                // commonly unavailable to desktop applications. DACL/owner/group failures are
                // intentionally not caught because replacement must not proceed without them.
            }
        }

        File.SetCreationTimeUtc(destination, creationTimeUtc);
        File.SetLastWriteTimeUtc(destination, lastWriteTimeUtc);
        File.SetLastAccessTimeUtc(destination, lastAccessTimeUtc);
        File.SetAttributes(destination, attributes);
    }

    public void CopyDirectoryMetadata(string source, string destination)
    {
        var sourceInfo = new DirectoryInfo(source);

        // Copy directory attributes and timestamps
        File.SetAttributes(destination, sourceInfo.Attributes);
        Directory.SetCreationTime(destination, sourceInfo.CreationTime);
        Directory.SetLastWriteTime(destination, sourceInfo.LastWriteTime);
        Directory.SetLastAccessTime(destination, sourceInfo.LastAccessTime);
    }

    public void SafeDelete(string path)
    {
        if (!FileExists(path))
        {
            return;
        }

        // Reset attributes in case file is read-only
        File.SetAttributes(path, FileAttributes.Normal);
        DeleteFile(path);
    }

    public void SafeDeleteDirectory(string path)
    {
        if (!DirectoryExists(path))
        {
            return;
        }

        // Recursively reset attributes before deletion
        ResetAttributesRecursive(path);
        FileSystem.DeleteDirectory(path, UIOption.AllDialogs, RecycleOption.SendToRecycleBin);
    }

    public string GetFileExtension(string path)
    {
        return Path.GetExtension(path);
    }

    public string? RenameFolder(string? newName, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ServiceException("Path cannot be null or empty");
        }

        try
        {
            var directoryInfo = GetDirectoryInfo(path);
            if (!directoryInfo.Exists)
            {
                return directoryInfo.Name;
            }

            if (string.IsNullOrWhiteSpace(newName))
            {
                return directoryInfo.Name;
            }

            var parentDirectory = directoryInfo.Parent;
            if (parentDirectory == null)
            {
                return directoryInfo.Name;
            }

            var newPath = Path.Combine(parentDirectory.FullName, newName);
            if (DirectoryExists(newPath))
            {
                return directoryInfo.Name;
            }

            FileSystem.RenameDirectory(path, newName);
            return newName;
        }
        catch (Exception ex) when (ex is not ServiceException)
        {
            throw new ServiceException($"Failed to rename folder from '{path}' to '{newName}'", ex);
        }
    }

    private static void CopyAlternateDataStreams(string source, string destination)
    {
        var streamData = new Win32FindStreamData();
        using var findHandle = FindFirstStreamW(source, 0, streamData, 0);
        if (findHandle.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            if (error is ErrorHandleEof or ErrorInvalidFunction or ErrorNotSupported)
            {
                return;
            }

            throw new Win32Exception(error, $"Failed to enumerate alternate data streams for '{source}'.");
        }

        while (true)
        {
            var streamName = streamData.StreamName;
            if (!string.Equals(streamName, DefaultDataStreamName, StringComparison.OrdinalIgnoreCase))
            {
                CopyAlternateDataStream(source, destination, streamName);
            }

            if (FindNextStreamW(findHandle, streamData))
            {
                continue;
            }

            var error = Marshal.GetLastWin32Error();
            if (error is ErrorNoMoreFiles or ErrorHandleEof)
            {
                break;
            }

            throw new Win32Exception(error, $"Failed to enumerate alternate data streams for '{source}'.");
        }
    }

    private static void CopyAlternateDataStream(string source, string destination, string streamName)
    {
        // FindFirstStreamW returns names such as ":metadata:$DATA". Appending the full
        // stream name addresses that stream without ever reopening or copying "::$DATA".
        using var input = new FileStream(
            source + streamName,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.SequentialScan);
        using var output = new FileStream(
            destination + streamName,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            64 * 1024,
            FileOptions.WriteThrough);
        input.CopyTo(output);
        output.Flush(flushToDisk: true);
    }

    private static bool IsAuditMetadataUnavailable(Exception exception) =>
        exception is PrivilegeNotHeldException
            or UnauthorizedAccessException
            or PlatformNotSupportedException
            or NotSupportedException
        || exception is Win32Exception { NativeErrorCode: ErrorAccessDenied or ErrorPrivilegeNotHeld or ErrorNotSupported };

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFindHandle FindFirstStreamW(
        string fileName,
        int informationLevel,
        [In] [Out] Win32FindStreamData streamData,
        uint flags);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FindNextStreamW(
        SafeFindHandle findStream,
        [In] [Out] Win32FindStreamData streamData);

    private const string DefaultDataStreamName = "::$DATA";
    private const int ErrorInvalidFunction = 1;
    private const int ErrorAccessDenied = 5;
    private const int ErrorNoMoreFiles = 18;
    private const int ErrorHandleEof = 38;
    private const int ErrorNotSupported = 50;
    private const int ErrorPrivilegeNotHeld = 1314;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private sealed class Win32FindStreamData
    {
        public long StreamSize;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 296)]
        public string StreamName = string.Empty;
    }

    private void ResetAttributesRecursive(string path)
    {
        // Reset attributes for the directory itself
        File.SetAttributes(path, FileAttributes.Normal);

        // Reset attributes for all files in directory
        foreach (var file in GetFiles(path))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        // Recursively process subdirectories
        foreach (var dir in GetDirectories(path))
        {
            ResetAttributesRecursive(dir);
        }
    }
}
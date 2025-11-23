using System.Runtime.InteropServices;

namespace ImageCare.Core.Services.FileSystemService.Windows;

[Serializable]
public class FileData
{
    internal FileData(string dir, Win32FindData findData)
    {
        Attributes = findData.dwFileAttributes;
        CreationTimeUtc = ConvertDateTime(findData.ftCreationTime_dwHighDateTime, findData.ftCreationTime_dwLowDateTime);
        LastAccessTimeUtc = ConvertDateTime(findData.ftLastAccessTime_dwHighDateTime, findData.ftLastAccessTime_dwLowDateTime);
        LastWriteTimeUtc = ConvertDateTime(findData.ftLastWriteTime_dwHighDateTime, findData.ftLastWriteTime_dwLowDateTime);
        Size = CombineHighLowInts(findData.nFileSizeHigh, findData.nFileSizeLow);
        Name = findData.cFileName;
        Path = System.IO.Path.Combine(dir, findData.cFileName);
    }

    public FileAttributes Attributes { get; }

    public DateTime CreationTime => CreationTimeUtc.ToLocalTime();

    public DateTime CreationTimeUtc { get; }

    public DateTime LastAccessTime => LastAccessTimeUtc.ToLocalTime();

    public DateTime LastAccessTimeUtc { get; }

    public DateTime LastWriteTime => LastWriteTimeUtc.ToLocalTime();

    public DateTime LastWriteTimeUtc { get; }

    public long Size { get; }

    public string Name { get; }

    public string Path { get; }

    public override string ToString()
    {
        return Name;
    }

    private static long CombineHighLowInts(uint high, uint low)
    {
        return ((long)high << 32) | low;
    }

    private static DateTime ConvertDateTime(uint high, uint low)
    {
        var fileTime = CombineHighLowInts(high, low);
        return DateTime.FromFileTimeUtc(fileTime);
    }
}

[Serializable]
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
internal class Win32FindData
{
    public FileAttributes dwFileAttributes;
    public uint ftCreationTime_dwLowDateTime;
    public uint ftCreationTime_dwHighDateTime;
    public uint ftLastAccessTime_dwLowDateTime;
    public uint ftLastAccessTime_dwHighDateTime;
    public uint ftLastWriteTime_dwLowDateTime;
    public uint ftLastWriteTime_dwHighDateTime;
    public uint nFileSizeHigh;
    public uint nFileSizeLow;
    public int dwReserved0;
    public int dwReserved1;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
    public string cFileName = string.Empty;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
    public string cAlternateFileName = string.Empty;

    public override string ToString()
    {
        return cFileName;
    }
}
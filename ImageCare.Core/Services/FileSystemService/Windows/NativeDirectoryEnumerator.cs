using System.Collections;
using System.Runtime.InteropServices;

namespace ImageCare.Core.Services.FileSystemService.Windows;

public static class NativeDirectoryEnumerator
{
    public static IEnumerable<FileData> EnumerateFiles(string path)
    {
        return EnumerateFiles(path, "*");
    }

    public static IEnumerable<FileData> EnumerateFiles(string path, string searchPattern)
    {
        return EnumerateFiles(path, searchPattern, SearchOption.TopDirectoryOnly);
    }

    public static IEnumerable<FileData> EnumerateFiles(string path, string searchPattern, SearchOption searchOption)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(searchPattern);

        if (searchOption is not (SearchOption.TopDirectoryOnly or SearchOption.AllDirectories))
        {
            throw new ArgumentOutOfRangeException(nameof(searchOption));
        }

        var fullPath = Path.GetFullPath(path);
        return new FileEnumerable(fullPath, searchPattern, searchOption);
    }

    public static FileData[] GetFiles(string path, string searchPattern, SearchOption searchOption)
    {
        return EnumerateFiles(path, searchPattern, searchOption).ToArray();
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern SafeFindHandle FindFirstFile(string fileName, [In] [Out] Win32FindData data);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool FindNextFile(SafeFindHandle hndFindFile, [In] [Out] [MarshalAs(UnmanagedType.LPStruct)] Win32FindData lpFindFileData);

    private class FileEnumerable : IEnumerable<FileData>
    {
        private readonly string _path;
        private readonly string _filter;
        private readonly SearchOption _searchOption;

        public FileEnumerable(string path, string filter, SearchOption searchOption)
        {
            _path = path;
            _filter = filter;
            _searchOption = searchOption;
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public IEnumerator<FileData> GetEnumerator()
        {
            return new FileEnumerator(_path, _filter, _searchOption);
        }
    }

    private class FileEnumerator : IEnumerator<FileData>
    {
        private readonly string _filter;
        private readonly SearchOption _searchOption;
        private readonly Stack<SearchContext>? _contextStack;
        private readonly Win32FindData _winFindData = new();

        private string _path;
        private SearchContext _currentContext;
        private SafeFindHandle? _hndFindFile;

        public FileEnumerator(string path, string filter, SearchOption searchOption)
        {
            _path = path;
            _filter = filter;
            _searchOption = searchOption;
            _currentContext = new SearchContext(path);

            if (_searchOption == SearchOption.AllDirectories)
            {
                _contextStack = new Stack<SearchContext>();
            }
        }

        public FileData Current => new(_path, _winFindData);

        object IEnumerator.Current => Current;

        public void Dispose()
        {
            _hndFindFile?.Dispose();
        }

        public bool MoveNext()
        {
            var retval = false;

            if (_currentContext.SubdirectoriesToProcess == null)
            {
                if (_hndFindFile == null)
                {
                    var searchPath = Path.Combine(_path, _filter);
                    _hndFindFile = FindFirstFile(searchPath, _winFindData);
                    retval = !_hndFindFile.IsInvalid;
                }
                else
                {
                    retval = FindNextFile(_hndFindFile, _winFindData);
                }
            }

            if (retval)
            {
                if ((_winFindData.dwFileAttributes & FileAttributes.Directory) == FileAttributes.Directory)
                {
                    return MoveNext();
                }
            }
            else if (_searchOption == SearchOption.AllDirectories)
            {
                if (_currentContext.SubdirectoriesToProcess == null)
                {
                    var subDirectories = Directory.GetDirectories(_path);
                    _currentContext.SubdirectoriesToProcess = new Stack<string>(subDirectories);
                }

                if (_currentContext.SubdirectoriesToProcess.Count > 0)
                {
                    var subDir = _currentContext.SubdirectoriesToProcess.Pop();

                    _contextStack!.Push(_currentContext);
                    _path = subDir;
                    _hndFindFile = null;
                    _currentContext = new SearchContext(_path);

                    return MoveNext();
                }

                if (_contextStack!.Count > 0)
                {
                    _currentContext = _contextStack.Pop();
                    _path = _currentContext.Path;
                    _hndFindFile?.Close();
                    _hndFindFile = null;

                    return MoveNext();
                }
            }

            return retval;
        }

        public void Reset()
        {
            _hndFindFile?.Close();
            _hndFindFile = null;
        }

        private class SearchContext
        {
            public readonly string Path;
            public Stack<string>? SubdirectoriesToProcess;

            public SearchContext(string path)
            {
                Path = path;
            }
        }
    }
}
using System.Collections.Concurrent;
using System.Reactive.Linq;
using System.Reactive.Subjects;

using ImageCare.Core.Domain;
using ImageCare.Core.Services.FileOperationsService.Native;

namespace ImageCare.Core.Services.FileOperationsService;

public sealed class LocalFileSystemFileOperationsService : IFileOperationsService, IDisposable
{
    private readonly Subject<SelectedMediaPreview> _selectedImagePreviewSubject;

    private readonly ConcurrentDictionary<FileManagerPanel, MediaPreview> _selectedImagePreviews = new();

    public LocalFileSystemFileOperationsService()
    {
        _selectedImagePreviewSubject = new Subject<SelectedMediaPreview>();
    }

    /// <inheritdoc />
    public IObservable<SelectedMediaPreview> ImagePreviewSelected => _selectedImagePreviewSubject.AsObservable();

    /// <inheritdoc />
    public void Dispose()
    {
        _selectedImagePreviewSubject.Dispose();
    }

    public Task<OperationResult> MoveWithProgressAsync(string source, string destination, IProgress<OperationInfo> progress, CancellationToken cancellationToken = default)
    {
        return Task.Run(
            () =>
            {
                var destinationPathCorrected = destination;
                if (IsDirFile(source) == false)
                {
                    destinationPathCorrected = CorrectFileDestinationPath(source, destination);
                }

                return MoveWithProgress(source, destinationPathCorrected, progress, cancellationToken);
            },
            cancellationToken);
    }

    public Task<OperationResult> CopyWithProgressAsync(string source, string destination, IProgress<OperationInfo> progress, CancellationToken cancellationToken = default)
    {
        return Task.Run(
            () =>
            {
                try
                {
                    return CopyWithProgress(source, destination, progress, cancellationToken);
                }
                catch
                {
                    return OperationResult.Failed;
                }
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<OperationResult> CopyImagePreviewToDirectoryAsync(MediaPreview imagePreview, string selectedFolderPath, Progress<OperationInfo> progress)
    {
        if (!Directory.Exists(selectedFolderPath))
        {
            return OperationResult.Failed;
        }

        var imagePreViewFileInfo = new FileInfo(imagePreview.Url);
        var destinationFilePath = Path.Combine(selectedFolderPath, imagePreViewFileInfo.Name);

        return await CopyWithProgressAsync(imagePreViewFileInfo.FullName, destinationFilePath, progress);
    }

    /// <inheritdoc />
    public async Task<OperationResult> MoveImagePreviewToDirectoryAsync(MediaPreview imagePreview, string selectedFolderPath, Progress<OperationInfo> progress)
    {
        if (!Directory.Exists(selectedFolderPath))
        {
            return OperationResult.Failed;
        }

        var imagePreViewFileInfo = new FileInfo(imagePreview.Url);
        var destinationFilePath = Path.Combine(selectedFolderPath, imagePreViewFileInfo.Name);

        return await MoveWithProgressAsync(imagePreViewFileInfo.FullName, destinationFilePath, progress);
    }

    /// <inheritdoc />
    public async Task<OperationResult> DeleteImagePreviewAsync(MediaPreview imagePreview)
    {
        if (!File.Exists(imagePreview.Url))
        {
            return OperationResult.Failed;
        }

        try
        {
            File.Delete(imagePreview.Url);

            return OperationResult.Success;
        }
        catch (Exception exception)
        {
            return OperationResult.Failed;
        }
    }

    /// <inheritdoc />
    public void SetSelectedPreview(SelectedMediaPreview selectedImagePreview)
    {
        _selectedImagePreviews.AddOrUpdate(selectedImagePreview.FileManagerPanel, _ => selectedImagePreview, (_, _) => selectedImagePreview);
        _selectedImagePreviewSubject.OnNext(selectedImagePreview);
    }

    private OperationResult MoveWithProgress(string source, string destination, IProgress<OperationInfo> progress, CancellationToken cancellationToken = default)
    {
        var startTimestamp = DateTime.Now;
        NativeMethods.CopyProgressRoutine lpProgressRoutine = (size, transferred, streamSize, bytesTransferred, number, reason, file, destinationFile, data) =>
        {
            var fileProgress = new OperationInfo(startTimestamp, bytesTransferred)
            {
                Total = size,
                Transferred = transferred,
                StreamSize = streamSize,
                BytesTransferred = bytesTransferred,
                ProcessedFile = source
            };
            try
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return NativeMethods.CopyProgressResult.PROGRESS_CANCEL;
                }

                progress.Report(fileProgress);
                return NativeMethods.CopyProgressResult.PROGRESS_CONTINUE;
            }
            catch (Exception)
            {
                return NativeMethods.CopyProgressResult.PROGRESS_STOP;
            }
        };

        if (cancellationToken.IsCancellationRequested)
        {
            return OperationResult.Cancelled;
        }

        if (!NativeMethods.MoveFileWithProgress(source, destination, lpProgressRoutine, IntPtr.Zero, NativeMethods.MoveFileFlags.MOVE_FILE_REPLACE_EXISTSING | NativeMethods.MoveFileFlags.MOVE_FILE_COPY_ALLOWED | NativeMethods.MoveFileFlags.MOVE_FILE_WRITE_THROUGH))
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return OperationResult.Cancelled;
            }

            return OperationResult.Failed;
        }

        return OperationResult.Success;
    }

    private OperationResult CopyFileWithProgress(string sourceFile, string newFile, IProgress<OperationInfo> progress, CancellationToken cancellationToken)
    {
        var pbCancel = 0;
        var startTimestamp = DateTime.Now;

        NativeMethods.CopyProgressRoutine lpProgressRoutine = (size, transferred, streamSize, bytesTransferred, number, reason, file, destinationFile, data) =>
        {
            var fileProgress = new OperationInfo(startTimestamp, bytesTransferred)
            {
                Total = size,
                Transferred = transferred,
                StreamSize = streamSize,
                ProcessedFile = sourceFile
            };
            try
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return NativeMethods.CopyProgressResult.PROGRESS_CANCEL;
                }

                progress.Report(fileProgress);
                return NativeMethods.CopyProgressResult.PROGRESS_CONTINUE;
            }
            catch (Exception)
            {
                return NativeMethods.CopyProgressResult.PROGRESS_STOP;
            }
        };
        if (cancellationToken.IsCancellationRequested)
        {
            return OperationResult.Cancelled;
        }

        var ctr = cancellationToken.Register(() => pbCancel = 1);

        var result = NativeMethods.CopyFileEx(sourceFile, newFile, lpProgressRoutine, IntPtr.Zero, ref pbCancel, NativeMethods.CopyFileFlags.COPY_FILE_FAIL_IF_EXISTS);
        if (cancellationToken.IsCancellationRequested)
        {
            return OperationResult.Cancelled;
        }

        return result ? OperationResult.Success : OperationResult.Failed;
    }

    private OperationResult CopyWithProgress(string source, string destination, IProgress<OperationInfo> progress, CancellationToken cancellationToken = default)
    {
        var isDir = IsDirFile(source);
        if (isDir == null)
        {
            throw new ArgumentException("Source parameter has to be file or directory! " + source);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return OperationResult.Cancelled;
        }

        var destinationFile = CorrectFileDestinationPath(source, destination);

        return CopyFileWithProgress(source, destinationFile, progress, cancellationToken);
    }

    private static bool? IsDirFile(string path)
    {
        bool? result = null;
        if (Directory.Exists(path) || File.Exists(path))
        {
            // get the file attributes for file or directory 
            var fileAttr = File.GetAttributes(path);
            result = fileAttr.HasFlag(FileAttributes.Directory);
        }

        return result;
    }

    private static string CorrectFileDestinationPath(string source, string destination)
    {
        var destinationFile = destination;
        if (IsDirFile(destination) == true)
        {
            destinationFile = Path.Combine(destination, Path.GetFileName(source));
        }

        return destinationFile;
    }
}
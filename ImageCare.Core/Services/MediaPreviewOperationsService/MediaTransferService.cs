using ImageCare.Core.Exceptions;
using ImageCare.Core.Services.FileSystemService;
using ImageCare.Core.Services.MediaAnnotationsService;

namespace ImageCare.Core.Services.MediaPreviewOperationsService;

internal sealed record MediaTransferResult(string SourcePath, string DestinationPath);

internal sealed class MediaTransferService
{
    private readonly IFileSystemService _fileSystemService;

    public MediaTransferService(IFileSystemService fileSystemService)
    {
        _fileSystemService = fileSystemService ?? throw new ArgumentNullException(nameof(fileSystemService));
    }

    public MediaTransferResult CopyToDirectory(
        string sourcePath,
        string destinationDirectory,
        string? destinationFileName = null)
    {
        var bundle = ResolveTransfer(sourcePath, destinationDirectory, destinationFileName);
        CopyBundle(
            bundle.SourcePath,
            bundle.DestinationPath,
            bundle.SourceSidecarPath,
            bundle.DestinationSidecarPath);
        return new MediaTransferResult(bundle.SourcePath, bundle.DestinationPath);
    }

    public MediaTransferResult MoveToDirectory(
        string sourcePath,
        string destinationDirectory,
        string? destinationFileName = null)
    {
        var bundle = ResolveTransfer(sourcePath, destinationDirectory, destinationFileName);
        MoveBundle(
            bundle.SourcePath,
            bundle.DestinationPath,
            bundle.SourceSidecarPath,
            bundle.DestinationSidecarPath);
        return new MediaTransferResult(bundle.SourcePath, bundle.DestinationPath);
    }

    public void CopyBundle(
        string sourcePath,
        string destinationPath,
        string? sourceSidecarPath,
        string? destinationSidecarPath)
    {
        var sidecarCreated = false;
        var mediaCreated = false;
        try
        {
            if (sourceSidecarPath is not null && destinationSidecarPath is not null)
            {
                _fileSystemService.CopyFile(sourceSidecarPath, destinationSidecarPath);
                sidecarCreated = true;
            }

            _fileSystemService.CopyFile(sourcePath, destinationPath);
            mediaCreated = true;
        }
        catch (Exception operationException)
        {
            var rollbackExceptions = new List<Exception>();
            if (mediaCreated)
            {
                TryDeleteRollbackDestination(destinationPath, rollbackExceptions);
            }

            if (sidecarCreated)
            {
                TryDeleteRollbackDestination(destinationSidecarPath, rollbackExceptions);
            }

            ThrowWithRollbackFailures(operationException, rollbackExceptions);
            throw;
        }
    }

    public void MoveBundle(
        string sourcePath,
        string destinationPath,
        string? sourceSidecarPath,
        string? destinationSidecarPath)
    {
        var sidecarMoved = false;
        var mediaMoved = false;
        try
        {
            if (sourceSidecarPath is not null && destinationSidecarPath is not null)
            {
                MoveFileReliably(sourceSidecarPath, destinationSidecarPath);
                sidecarMoved = true;
            }

            MoveFileReliably(sourcePath, destinationPath);
            mediaMoved = true;
        }
        catch (Exception operationException)
        {
            var rollbackExceptions = new List<Exception>();
            if (mediaMoved)
            {
                TryRollbackMove(destinationPath, sourcePath, rollbackExceptions);
            }

            if (sidecarMoved)
            {
                TryRollbackMove(destinationSidecarPath, sourceSidecarPath, rollbackExceptions);
            }

            ThrowWithRollbackFailures(operationException, rollbackExceptions);
            throw;
        }
    }

    public string GetUniqueFileName(
        string directory,
        string fileName,
        string? sidecarOwnerMediaPath = null)
    {
        var files = _fileSystemService.GetFiles(directory);
        var existingFiles = new HashSet<string>(files, StringComparer.OrdinalIgnoreCase);
        var extension = Path.GetExtension(fileName);
        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
        var counter = 0;

        while (true)
        {
            var candidateFileName = counter == 0
                ? fileName
                : $"{fileNameWithoutExtension} ({counter}){extension}";
            var candidatePath = Path.Combine(directory, candidateFileName);
            var sidecarPath = sidecarOwnerMediaPath is null
                ? null
                : XmpSidecarPath.GetDestinationExternalSidecarPath(
                    sidecarOwnerMediaPath,
                    candidatePath);
            var destinationExists = existingFiles.Contains(candidatePath)
                || _fileSystemService.FileExists(candidatePath)
                || XmpSidecarPath.HasAnotherRawOwner(candidatePath)
                || (sidecarPath is not null
                    && (existingFiles.Contains(sidecarPath)
                        || _fileSystemService.FileExists(sidecarPath)));
            if (!destinationExists)
            {
                return candidateFileName;
            }

            counter++;
        }
    }

    public bool BundleDestinationExists(string destinationPath, string? sidecarOwnerMediaPath)
    {
        if (_fileSystemService.FileExists(destinationPath)
            || XmpSidecarPath.HasAnotherRawOwner(destinationPath))
        {
            return true;
        }

        var sidecarPath = sidecarOwnerMediaPath is null
            ? null
            : XmpSidecarPath.GetDestinationExternalSidecarPath(
                sidecarOwnerMediaPath,
                destinationPath);
        return sidecarPath is not null && _fileSystemService.FileExists(sidecarPath);
    }

    private TransferBundle ResolveTransfer(
        string sourcePath,
        string destinationDirectory,
        string? requestedFileName)
    {
        if (!_fileSystemService.DirectoryExists(destinationDirectory))
        {
            throw new ServiceException($"Destination directory does not exist: {destinationDirectory}");
        }

        if (!_fileSystemService.FileExists(sourcePath))
        {
            throw new ServiceException($"Source file does not exist: {sourcePath}");
        }

        var sourceSidecarPath = XmpSidecarPath.GetExistingExternalSidecarPath(
            sourcePath,
            _fileSystemService.FileExists);
        var fileName = GetDestinationFileName(
            destinationDirectory,
            sourcePath,
            requestedFileName,
            sourceSidecarPath is not null);
        var destinationPath = Path.Combine(destinationDirectory, fileName);
        var destinationSidecarPath = sourceSidecarPath is null
            ? null
            : XmpSidecarPath.GetDestinationExternalSidecarPath(sourcePath, destinationPath);

        return new TransferBundle(
            sourcePath,
            destinationPath,
            sourceSidecarPath,
            destinationSidecarPath);
    }

    private string GetDestinationFileName(
        string destinationDirectory,
        string sourcePath,
        string? requestedFileName,
        bool hasExternalSidecar)
    {
        if (requestedFileName == null)
        {
            return GetUniqueFileName(
                destinationDirectory,
                Path.GetFileName(sourcePath),
                hasExternalSidecar ? sourcePath : null);
        }

        if (!PortableFileNameValidator.IsValid(requestedFileName))
        {
            throw new ServiceException($"Invalid destination file name: {requestedFileName}");
        }

        var destinationPath = Path.Combine(destinationDirectory, requestedFileName);
        if (BundleDestinationExists(
                destinationPath,
                hasExternalSidecar ? sourcePath : null))
        {
            throw new ServiceException($"Destination file or sidecar already exists: {destinationPath}");
        }

        return requestedFileName;
    }

    public void MoveFileReliably(string sourcePath, string destinationPath)
    {
        try
        {
            _fileSystemService.MoveFile(sourcePath, destinationPath);
        }
        catch (IOException) when (!_fileSystemService.FileExists(destinationPath))
        {
            var destinationCreated = false;
            try
            {
                _fileSystemService.CopyFile(sourcePath, destinationPath);
                destinationCreated = true;
                _fileSystemService.CopyFileMetadata(sourcePath, destinationPath);
                _fileSystemService.SafeDelete(sourcePath);
            }
            catch
            {
                if (destinationCreated && _fileSystemService.FileExists(destinationPath))
                {
                    _fileSystemService.SafeDelete(destinationPath);
                }

                throw;
            }
        }
    }

    private void TryDeleteRollbackDestination(
        string? path,
        ICollection<Exception> rollbackExceptions)
    {
        if (path is null || !_fileSystemService.FileExists(path))
        {
            return;
        }

        try
        {
            _fileSystemService.SafeDelete(path);
        }
        catch (Exception exception)
        {
            rollbackExceptions.Add(exception);
        }
    }

    private void TryRollbackMove(
        string? destinationPath,
        string? sourcePath,
        ICollection<Exception> rollbackExceptions)
    {
        if (destinationPath is null
            || sourcePath is null
            || !_fileSystemService.FileExists(destinationPath))
        {
            return;
        }

        try
        {
            if (_fileSystemService.FileExists(sourcePath))
            {
                _fileSystemService.SafeDelete(destinationPath);
            }
            else
            {
                MoveFileReliably(destinationPath, sourcePath);
            }
        }
        catch (Exception exception)
        {
            rollbackExceptions.Add(exception);
        }
    }

    private static void ThrowWithRollbackFailures(
        Exception operationException,
        IReadOnlyCollection<Exception> rollbackExceptions)
    {
        if (rollbackExceptions.Count > 0)
        {
            throw new AggregateException(
                "The file operation failed and one or more rollback actions also failed.",
                new[] { operationException }.Concat(rollbackExceptions));
        }
    }

    private sealed record TransferBundle(
        string SourcePath,
        string DestinationPath,
        string? SourceSidecarPath,
        string? DestinationSidecarPath);
}

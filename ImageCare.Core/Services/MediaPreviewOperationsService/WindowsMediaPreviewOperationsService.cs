using System.Collections.Concurrent;
using System.Reactive.Linq;
using System.Reactive.Subjects;

using ImageCare.Core.Domain.Folders;
using ImageCare.Core.Domain.Preview;
using ImageCare.Core.Exceptions;
using ImageCare.Core.Services.FileSystemService;
using ImageCare.Core.Services.ProcessService;

namespace ImageCare.Core.Services.MediaPreviewOperationsService;

public sealed class WindowsMediaPreviewOperationsService : IMediaPreviewOperationsService, IDisposable
{
	private readonly Subject<SelectedMediaPreview> _selectedImagePreviewSubject;
	private readonly IFileSystemService _fileSystemService;
	private readonly IProcessService _processService;
	private readonly ConcurrentDictionary<FileManagerPanel, MediaPreview> _selectedImagePreviews = new();
	private SelectedMediaPreview? _lastSelectedMediaPreview;

	public WindowsMediaPreviewOperationsService(IFileSystemService fileSystemService, IProcessService processService)
	{
		_fileSystemService = fileSystemService ?? throw new ArgumentNullException(nameof(fileSystemService));
		_processService = processService ?? throw new ArgumentNullException(nameof(processService));
		_selectedImagePreviewSubject = new Subject<SelectedMediaPreview>();
	}

	public IObservable<SelectedMediaPreview> ImagePreviewSelected => _selectedImagePreviewSubject.AsObservable();

	public void Dispose()
	{
		_selectedImagePreviewSubject.Dispose();
	}

	public async Task<OperationResult> CopyImagePreviewToDirectoryAsync(MediaPreview imagePreview,
	                                                                    string selectedFolderPath,
	                                                                    Progress<OperationInfo> progress)
	{
		try
		{
			if (!_fileSystemService.DirectoryExists(selectedFolderPath))
			{
				return OperationResult.Failed;
			}

			var fileName = GetUniqueFileName(selectedFolderPath, Path.GetFileName(imagePreview.Url));
			var destinationFilePath = Path.Combine(selectedFolderPath, fileName);

			return await CopyWithProgressAsync(imagePreview.Url, destinationFilePath, progress);
		}
		catch (Exception ex)
		{
			throw new ServiceException("Failed to copy image preview", ex);
		}
	}

	public async Task<OperationResult> MoveImagePreviewToDirectoryAsync(MediaPreview imagePreview,
	                                                                    string selectedFolderPath,
	                                                                    Progress<OperationInfo> progress)
	{
		try
		{
			if (!_fileSystemService.DirectoryExists(selectedFolderPath))
			{
				return OperationResult.Failed;
			}

			var fileName = GetUniqueFileName(selectedFolderPath, Path.GetFileName(imagePreview.Url));
			var destinationFilePath = Path.Combine(selectedFolderPath, fileName);

			return await MoveWithProgressAsync(imagePreview.Url, destinationFilePath, progress);
		}
		catch (Exception ex)
		{
			throw new ServiceException("Failed to move image preview", ex);
		}
	}

	public Task<OperationResult> DeleteImagePreviewAsync(MediaPreview imagePreview)
	{
		try
		{
			if (!_fileSystemService.FileExists(imagePreview.Url))
			{
				return Task.FromResult(OperationResult.Failed);
			}

			_fileSystemService.SafeDelete(imagePreview.Url);
			return Task.FromResult(OperationResult.Success);
		}
		catch (Exception ex)
		{
			throw new ServiceException("Failed to delete image preview", ex);
		}
	}

	public void SetSelectedPreview(SelectedMediaPreview selectedImagePreview)
	{
		_selectedImagePreviews.AddOrUpdate(
			selectedImagePreview.FileManagerPanel,
			_ => selectedImagePreview,
			(_, _) => selectedImagePreview);

		_lastSelectedMediaPreview = selectedImagePreview;
		_selectedImagePreviewSubject.OnNext(selectedImagePreview);
	}

	public void OpenInExternalProcess(MediaPreview mediaPreview, string pathToExecutable)
	{
		try
		{
			if (!_fileSystemService.FileExists(pathToExecutable))
			{
				throw new FileNotFoundException($"Executable not found: {pathToExecutable}");
			}

			if (!_fileSystemService.FileExists(mediaPreview.Url))
			{
				throw new FileNotFoundException($"Media file not found: {mediaPreview.Url}");
			}

			_processService.StartProcess(pathToExecutable, $"\"{mediaPreview.Url}\"");
		}
		catch (Exception ex)
		{
			throw new ServiceException("Failed to open in external process", ex);
		}
	}

	public MediaPreview? GetLastSelectedMediaPreview()
	{
		return _lastSelectedMediaPreview;
	}

	private async Task<OperationResult> MoveWithProgressAsync(string source,
	                                                          string destination,
	                                                          IProgress<OperationInfo> progress,
	                                                          CancellationToken cancellationToken = default)
	{
		try
		{
			var destinationPathCorrected = _fileSystemService.IsDirectory(source)
				                               ? destination
				                               : CorrectFileDestinationPath(source, destination);

			return await MoveWithProgressInternalAsync(source, destinationPathCorrected, progress, cancellationToken);
		}
		catch (Exception ex) when (ex is OperationCanceledException or TaskCanceledException)
		{
			return OperationResult.Cancelled;
		}
		catch (Exception ex)
		{
			throw new ServiceException("Failed to move with progress", ex);
		}
	}

	private async Task<OperationResult> CopyWithProgressAsync(string source,
	                                                          string destination,
	                                                          IProgress<OperationInfo> progress,
	                                                          CancellationToken cancellationToken = default)
	{
		try
		{
			var destinationPathCorrected = _fileSystemService.IsDirectory(source)
				                               ? destination
				                               : CorrectFileDestinationPath(source, destination);

			return await CopyWithProgressInternalAsync(source, destinationPathCorrected, progress, cancellationToken);
		}
		catch (Exception ex) when (ex is OperationCanceledException or TaskCanceledException)
		{
			return OperationResult.Cancelled;
		}
		catch (Exception ex)
		{
			throw new ServiceException("Failed to copy with progress", ex);
		}
	}

	private async Task<OperationResult> MoveWithProgressInternalAsync(string source,
	                                                                  string destination,
	                                                                  IProgress<OperationInfo> progress,
	                                                                  CancellationToken cancellationToken)
	{
		if (_fileSystemService.IsDirectory(source))
		{
			return await MoveDirectoryWithProgressAsync(source, destination, progress, cancellationToken);
		}

		return await MoveFileWithProgressAsync(source, destination, progress, cancellationToken);
	}

	private async Task<OperationResult> CopyWithProgressInternalAsync(string source,
	                                                                  string destination,
	                                                                  IProgress<OperationInfo> progress,
	                                                                  CancellationToken cancellationToken)
	{
		if (_fileSystemService.IsDirectory(source))
		{
			return await CopyDirectoryWithProgressAsync(source, destination, progress, cancellationToken);
		}

		return await CopyFileWithProgressAsync(source, destination, progress, cancellationToken);
	}

	private async Task<OperationResult> MoveFileWithProgressAsync(string sourceFile,
	                                                              string destinationFile,
	                                                              IProgress<OperationInfo> progress,
	                                                              CancellationToken cancellationToken)
	{
		try
		{
			if (!_fileSystemService.FileExists(sourceFile))
			{
				return OperationResult.Failed;
			}

			var copyResult = await CopyFileWithProgressAsync(sourceFile, destinationFile, progress, cancellationToken);
			if (copyResult != OperationResult.Success)
			{
				return copyResult;
			}

			_fileSystemService.SafeDelete(sourceFile);
			return OperationResult.Success;
		}
		catch (Exception ex)
		{
			throw new ServiceException("Failed to move file with progress", ex);
		}
	}

	private async Task<OperationResult> CopyFileWithProgressAsync(string sourceFile,
	                                                              string destinationFile,
	                                                              IProgress<OperationInfo> progress,
	                                                              CancellationToken cancellationToken)
	{
		const int bufferSize = 81920;
		var startTime = DateTime.Now;
		var fileInfo = _fileSystemService.GetFileInfo(sourceFile);

		try
		{
			await using var sourceStream = _fileSystemService.OpenRead(sourceFile);
			await using var destinationStream = _fileSystemService.Create(destinationFile);

			var buffer = new byte[bufferSize];
			long totalBytesRead = 0;
			int bytesRead;

			while ((bytesRead = await sourceStream.ReadAsync(buffer, cancellationToken)) > 0)
			{
				cancellationToken.ThrowIfCancellationRequested();
				await destinationStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
				totalBytesRead += bytesRead;

				progress?.Report(
					new OperationInfo(startTime, totalBytesRead)
					{
						Total = fileInfo.Length,
						Transferred = totalBytesRead,
						ProcessedFile = sourceFile
					});
			}

			_fileSystemService.CopyFileMetadata(sourceFile, destinationFile);
			return OperationResult.Success;
		}
		catch (OperationCanceledException)
		{
			_fileSystemService.SafeDelete(destinationFile);
			return OperationResult.Cancelled;
		}
		catch (Exception ex)
		{
			_fileSystemService.SafeDelete(destinationFile);
			throw new ServiceException("Failed to copy file with progress", ex);
		}
	}

	private async Task<OperationResult> MoveDirectoryWithProgressAsync(string sourceDir,
	                                                                   string destinationDir,
	                                                                   IProgress<OperationInfo> progress,
	                                                                   CancellationToken cancellationToken)
	{
		try
		{
			if (!_fileSystemService.DirectoryExists(sourceDir))
			{
				return OperationResult.Failed;
			}

			var copyResult = await CopyDirectoryWithProgressAsync(sourceDir, destinationDir, progress, cancellationToken);
			if (copyResult == OperationResult.Success)
			{
				_fileSystemService.SafeDeleteDirectory(sourceDir);
				return OperationResult.Success;
			}

			return copyResult;
		}
		catch (Exception ex)
		{
			throw new ServiceException("Failed to move directory with progress", ex);
		}
	}

	private async Task<OperationResult> CopyDirectoryWithProgressAsync(string sourceDir,
	                                                                   string destinationDir,
	                                                                   IProgress<OperationInfo> progress,
	                                                                   CancellationToken cancellationToken)
	{
		if (!_fileSystemService.DirectoryExists(sourceDir))
		{
			return OperationResult.Failed;
		}

		_fileSystemService.CreateDirectory(destinationDir);

		var files = _fileSystemService.GetFiles(sourceDir);
		var directories = _fileSystemService.GetDirectories(sourceDir);

		// Copy files
		foreach (var file in files)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var destFile = Path.Combine(destinationDir, Path.GetFileName(file));

			var result = await CopyFileWithProgressAsync(file, destFile, progress, cancellationToken);
			if (result != OperationResult.Success)
			{
				return result;
			}
		}

		// Copy subdirectories recursively
		foreach (var dir in directories)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var destDir = Path.Combine(destinationDir, Path.GetFileName(dir));

			var result = await CopyDirectoryWithProgressAsync(dir, destDir, progress, cancellationToken);
			if (result != OperationResult.Success)
			{
				return result;
			}
		}

		_fileSystemService.CopyDirectoryMetadata(sourceDir, destinationDir);
		return OperationResult.Success;
	}

	private string GetUniqueFileName(string directory, string fileName)
	{
		var files = _fileSystemService.GetFiles(directory);
		var existingFiles = new HashSet<string>(files, StringComparer.OrdinalIgnoreCase);

		if (!existingFiles.Contains(Path.Combine(directory, fileName)))
		{
			return fileName;
		}

		var extension = Path.GetExtension(fileName);
		var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
		var counter = 1;

		while (true)
		{
			var newFileName = $"{fileNameWithoutExtension} ({counter}){extension}";
			var fullPath = Path.Combine(directory, newFileName);

			if (!existingFiles.Contains(fullPath))
			{
				return newFileName;
			}

			counter++;
		}
	}

	private string CorrectFileDestinationPath(string source, string destination)
	{
		return _fileSystemService.DirectoryExists(destination)
			       ? Path.Combine(destination, Path.GetFileName(source))
			       : destination;
	}
}
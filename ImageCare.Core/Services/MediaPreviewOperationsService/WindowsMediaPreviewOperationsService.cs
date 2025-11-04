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

	public Task<OperationResult> CopyImagePreviewToDirectoryAsync(MediaPreview imagePreview,
	                                                              string selectedFolderPath,
	                                                              Progress<OperationInfo> progress)
	{
		try
		{
			if (!_fileSystemService.DirectoryExists(selectedFolderPath))
			{
				throw new ServiceException($"Destination directory does not exist: {selectedFolderPath}");
			}

			if (!_fileSystemService.FileExists(imagePreview.Url))
			{
				throw new ServiceException($"Source file does not exist: {imagePreview.Url}");
			}

			var fileName = GetUniqueFileName(selectedFolderPath, Path.GetFileName(imagePreview.Url));
			var destinationFilePath = Path.Combine(selectedFolderPath, fileName);

			_fileSystemService.CopyFile(imagePreview.Url, destinationFilePath);

			return Task.FromResult(OperationResult.Success);
		}
		catch (ServiceException)
		{
			throw;
		}
		catch (Exception ex)
		{
			throw new ServiceException("Failed to copy image preview", ex);
		}
	}

	public Task<OperationResult> MoveImagePreviewToDirectoryAsync(MediaPreview imagePreview,
	                                                              string selectedFolderPath,
	                                                              Progress<OperationInfo> progress)
	{
		try
		{
			if (!_fileSystemService.DirectoryExists(selectedFolderPath))
			{
				throw new ServiceException($"Destination directory does not exist: {selectedFolderPath}");
			}

			if (!_fileSystemService.FileExists(imagePreview.Url))
			{
				throw new ServiceException($"Source file does not exist: {imagePreview.Url}");
			}

			var fileName = GetUniqueFileName(selectedFolderPath, Path.GetFileName(imagePreview.Url));
			var destinationFilePath = Path.Combine(selectedFolderPath, fileName);

			_fileSystemService.MoveFile(imagePreview.Url, destinationFilePath);

			return Task.FromResult(OperationResult.Success);
		}
		catch (ServiceException)
		{
			throw;
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
				throw new ServiceException($"File does not exist: {imagePreview.Url}");
			}

			_fileSystemService.SafeDelete(imagePreview.Url);
			return Task.FromResult(OperationResult.Success);
		}
		catch (ServiceException)
		{
			throw;
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
				throw new ServiceException($"Executable not found: {pathToExecutable}");
			}

			if (!_fileSystemService.FileExists(mediaPreview.Url))
			{
				throw new ServiceException($"Media file not found: {mediaPreview.Url}");
			}

			_processService.StartProcess(pathToExecutable, $"\"{mediaPreview.Url}\"");
		}
		catch (ServiceException)
		{
			throw;
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
}
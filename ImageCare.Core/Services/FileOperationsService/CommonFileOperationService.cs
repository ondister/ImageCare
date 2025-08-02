using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reactive.Linq;
using System.Reactive.Subjects;

using ImageCare.Core.Domain.Folders;
using ImageCare.Core.Domain.Preview;

namespace ImageCare.Core.Services.FileOperationsService;

public sealed class CommonFileOperationService : IFileOperationsService, IDisposable
{
	private readonly Subject<SelectedMediaPreview> _selectedImagePreviewSubject;
	private readonly ConcurrentDictionary<FileManagerPanel, MediaPreview> _selectedImagePreviews = new();
	private SelectedMediaPreview _lastSelectedMediaPreview;

	public CommonFileOperationService()
	{
		_selectedImagePreviewSubject = new Subject<SelectedMediaPreview>();
	}

	public IObservable<SelectedMediaPreview> ImagePreviewSelected => _selectedImagePreviewSubject.AsObservable();

	public void Dispose()
	{
		_selectedImagePreviewSubject.Dispose();
	}

	public async Task<OperationResult> MoveWithProgressAsync(string source, string destination, IProgress<OperationInfo> progress, CancellationToken cancellationToken = default)
	{
		try
		{
			var destinationPathCorrected = IsDirectory(source)
				                               ? destination
				                               : CorrectFileDestinationPath(source, destination);

			return await MoveWithProgressInternalAsync(source, destinationPathCorrected, progress, cancellationToken);
		}
		catch (Exception ex) when (ex is OperationCanceledException || ex is TaskCanceledException)
		{
			return OperationResult.Cancelled;
		}
		catch (Exception)
		{
			return OperationResult.Failed;
		}
	}

	public async Task<OperationResult> CopyWithProgressAsync(string source, string destination, IProgress<OperationInfo> progress, CancellationToken cancellationToken = default)
	{
		try
		{
			var destinationPathCorrected = IsDirectory(source)
				                               ? destination
				                               : CorrectFileDestinationPath(source, destination);

			return await CopyWithProgressInternalAsync(source, destinationPathCorrected, progress, cancellationToken);
		}
		catch (Exception ex) when (ex is OperationCanceledException || ex is TaskCanceledException)
		{
			return OperationResult.Cancelled;
		}
		catch (Exception)
		{
			return OperationResult.Failed;
		}
	}

	public async Task<OperationResult> CopyImagePreviewToDirectoryAsync(MediaPreview imagePreview, string selectedFolderPath, Progress<OperationInfo> progress)
	{
		if (!Directory.Exists(selectedFolderPath))
		{
			return OperationResult.Failed;
		}

		var imagePreViewFileInfo = new FileInfo(imagePreview.Url);
		var fileName = GetUniqueFileName(selectedFolderPath, imagePreViewFileInfo.Name);
		var destinationFilePath = Path.Combine(selectedFolderPath, fileName);

		return await CopyWithProgressAsync(imagePreViewFileInfo.FullName, destinationFilePath, progress);
	}

	public async Task<OperationResult> MoveImagePreviewToDirectoryAsync(MediaPreview imagePreview, string selectedFolderPath, Progress<OperationInfo> progress)
	{
		if (!Directory.Exists(selectedFolderPath))
		{
			return OperationResult.Failed;
		}

		var imagePreViewFileInfo = new FileInfo(imagePreview.Url);
		var fileName = GetUniqueFileName(selectedFolderPath, imagePreViewFileInfo.Name);
		var destinationFilePath = Path.Combine(selectedFolderPath, fileName);

		return await MoveWithProgressAsync(imagePreViewFileInfo.FullName, destinationFilePath, progress);
	}

	public Task<OperationResult> DeleteImagePreviewAsync(MediaPreview imagePreview)
	{
		try
		{
			if (!File.Exists(imagePreview.Url))
			{
				return Task.FromResult(OperationResult.Failed);
			}

			File.Delete(imagePreview.Url);

			return Task.FromResult(OperationResult.Success);
		}
		catch
		{
			return Task.FromResult(OperationResult.Failed);
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
		if (!File.Exists(pathToExecutable))
		{
			throw new FileNotFoundException(pathToExecutable);
		}

		if (!File.Exists(mediaPreview.Url))
		{
			throw new FileNotFoundException(mediaPreview.Url);
		}

		var processInfo = new ProcessStartInfo
		{
			UseShellExecute = true,
			FileName = pathToExecutable,
			Arguments = $"\"{mediaPreview.Url}\"" // Ensure paths with spaces are handled correctly
		};

		Process.Start(processInfo);
	}

	public MediaPreview? GetLastSelectedMediaPreview()
	{
		return _lastSelectedMediaPreview;
	}

	private async Task<OperationResult> MoveWithProgressInternalAsync(string source, string destination, IProgress<OperationInfo> progress, CancellationToken cancellationToken)
	{
		if (IsDirectory(source))
		{
			return await MoveDirectoryWithProgressAsync(source, destination, progress, cancellationToken);
		}

		return await MoveFileWithProgressAsync(source, destination, progress, cancellationToken);
	}

	private async Task<OperationResult> CopyWithProgressInternalAsync(string source, string destination, IProgress<OperationInfo> progress, CancellationToken cancellationToken)
	{
		if (IsDirectory(source))
		{
			return await CopyDirectoryWithProgressAsync(source, destination, progress, cancellationToken);
		}

		return await CopyFileWithProgressAsync(source, destination, progress, cancellationToken);
	}

	private async Task<OperationResult> MoveFileWithProgressAsync(string sourceFile, string destinationFile, IProgress<OperationInfo> progress, CancellationToken cancellationToken)
	{
		// First try to use File.Move which is atomic when possible
		try
		{
			if (!File.Exists(sourceFile))
			{
				return OperationResult.Failed;
			}

			if (File.Exists(destinationFile))
			{
				File.Delete(destinationFile);
			}

			File.Move(sourceFile, destinationFile);

			return OperationResult.Success;
		}
		catch
		{
			// If direct move fails, fall back to copy + delete
			var copyResult = await CopyFileWithProgressAsync(sourceFile, destinationFile, progress, cancellationToken);
			if (copyResult == OperationResult.Success)
			{
				try
				{
					File.Delete(sourceFile);
					return OperationResult.Success;
				}
				catch
				{
					// If delete fails, try to rollback
					try
					{
						File.Delete(destinationFile);
					}
					catch { }

					return OperationResult.Failed;
				}
			}

			return copyResult;
		}
	}

	private async Task<OperationResult> CopyFileWithProgressAsync(string sourceFile, string destinationFile, IProgress<OperationInfo> progress, CancellationToken cancellationToken)
	{
		const int bufferSize = 81920; // 80KB buffer - optimal for most scenarios
		var startTime = DateTime.Now;
		var fileInfo = new FileInfo(sourceFile);

		try
		{
			await using (var sourceStream = new FileStream(sourceFile, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, true))
			await using (var destinationStream = new FileStream(destinationFile, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize, true))
			{
				var buffer = new byte[bufferSize];
				long totalBytesRead = 0;
				int bytesRead;

				while ((bytesRead = await sourceStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
				{
					cancellationToken.ThrowIfCancellationRequested();

					await destinationStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
					totalBytesRead += bytesRead;

					progress?.Report(
						new OperationInfo(startTime, totalBytesRead)
						{
							Total = fileInfo.Length,
							Transferred = totalBytesRead,
							ProcessedFile = sourceFile
						});
				}
			}

			// Preserve original file attributes and timestamps
			var attributes = File.GetAttributes(sourceFile);
			File.SetAttributes(destinationFile, attributes);
			File.SetCreationTime(destinationFile, fileInfo.CreationTime);
			File.SetLastWriteTime(destinationFile, fileInfo.LastWriteTime);
			File.SetLastAccessTime(destinationFile, fileInfo.LastAccessTime);

			return OperationResult.Success;
		}
		catch (OperationCanceledException)
		{
			// Clean up partially copied file
			try
			{
				if (File.Exists(destinationFile))
				{
					File.Delete(destinationFile);
				}
			}
			catch { }

			return OperationResult.Cancelled;
		}
		catch
		{
			// Clean up partially copied file
			try
			{
				if (File.Exists(destinationFile))
				{
					File.Delete(destinationFile);
				}
			}
			catch { }

			return OperationResult.Failed;
		}
	}

	private async Task<OperationResult> MoveDirectoryWithProgressAsync(string sourceDir, string destinationDir, IProgress<OperationInfo> progress, CancellationToken cancellationToken)
	{
		// First try to use Directory.Move which is atomic when possible
		try
		{
			if (!Directory.Exists(sourceDir))
			{
				return OperationResult.Failed;
			}

			if (Directory.Exists(destinationDir))
			{
				// On Unix systems, Directory.Move doesn't overwrite existing directories
				return OperationResult.Failed;
			}

			Directory.Move(sourceDir, destinationDir);
			return OperationResult.Success;
		}
		catch
		{
			// If direct move fails, fall back to copy + delete
			var copyResult = await CopyDirectoryWithProgressAsync(sourceDir, destinationDir, progress, cancellationToken);
			if (copyResult == OperationResult.Success)
			{
				try
				{
					Directory.Delete(sourceDir, true);
					return OperationResult.Success;
				}
				catch
				{
					// If delete fails, try to rollback
					try
					{
						Directory.Delete(destinationDir, true);
					}
					catch { }

					return OperationResult.Failed;
				}
			}

			return copyResult;
		}
	}

	private async Task<OperationResult> CopyDirectoryWithProgressAsync(string sourceDir, string destinationDir, IProgress<OperationInfo> progress, CancellationToken cancellationToken)
	{
		if (!Directory.Exists(sourceDir))
		{
			return OperationResult.Failed;
		}

		Directory.CreateDirectory(destinationDir);

		var files = Directory.GetFiles(sourceDir);
		var directories = Directory.GetDirectories(sourceDir);

		// Copy files first
		foreach (var file in files)
		{
			cancellationToken.ThrowIfCancellationRequested();

			var fileName = Path.GetFileName(file);
			var destFile = Path.Combine(destinationDir, fileName);

			var result = await CopyFileWithProgressAsync(file, destFile, progress, cancellationToken);
			if (result != OperationResult.Success)
			{
				return result;
			}
		}

		// Then copy subdirectories recursively
		foreach (var dir in directories)
		{
			cancellationToken.ThrowIfCancellationRequested();

			var dirName = Path.GetFileName(dir);
			var destDir = Path.Combine(destinationDir, dirName);

			var result = await CopyDirectoryWithProgressAsync(dir, destDir, progress, cancellationToken);
			if (result != OperationResult.Success)
			{
				return result;
			}
		}

		// Copy directory attributes
		var dirInfo = new DirectoryInfo(sourceDir);
		var destDirInfo = new DirectoryInfo(destinationDir)
		{
			Attributes = dirInfo.Attributes,
			CreationTime = dirInfo.CreationTime,
			LastWriteTime = dirInfo.LastWriteTime,
			LastAccessTime = dirInfo.LastAccessTime
		};

		return OperationResult.Success;
	}

	private static string GetUniqueFileName(string directory, string fileName)
	{
		if (!Directory.EnumerateFiles(directory, fileName, SearchOption.TopDirectoryOnly).Any())
		{
			return fileName;
		}

		var extension = Path.GetExtension(fileName);
		var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
		var counter = 1;

		while (true)
		{
			var newFileName = $"{fileNameWithoutExtension} ({counter}){extension}";
			if (!Directory.EnumerateFiles(directory, newFileName, SearchOption.TopDirectoryOnly).Any())
			{
				return newFileName;
			}

			counter++;
		}
	}

	private static bool IsDirectory(string path)
	{
		if (Directory.Exists(path))
		{
			return true;
		}

		if (File.Exists(path))
		{
			return false;
		}

		throw new FileNotFoundException("Path not found", path);
	}

	private static string CorrectFileDestinationPath(string source, string destination)
	{
		if (Directory.Exists(destination))
		{
			return Path.Combine(destination, Path.GetFileName(source));
		}

		return destination;
	}
}
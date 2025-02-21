using System.Collections.Concurrent;
using System.Reactive.Linq;
using System.Reactive.Subjects;

using ImageCare.Core.Domain.Folders;
using ImageCare.Core.Domain.MediaFormats;

using Microsoft.VisualBasic.FileIO;

using SearchOption = System.IO.SearchOption;

namespace ImageCare.Core.Services.FolderService;

public sealed class LocalFileSystemFolderService : IFolderService, IDisposable
{
	private readonly Subject<SelectedDirectory> _selectedDirectorySubject;
	private readonly Subject<SelectedDirectory> _folderVisitingSubject;
	private readonly Subject<SelectedDirectory> _folderLeftSubject;

	private readonly ConcurrentDictionary<FileManagerPanel, DirectoryModel> _selectedDirectories = new();
	private readonly ConcurrentDictionary<(string, FileManagerPanel), SelectedDirectory> _visitingDirectoryModels = new();
	private readonly DriveModelsFactory _driveModelsFactory;

	public LocalFileSystemFolderService()
	{
		_selectedDirectorySubject = new Subject<SelectedDirectory>();
		_folderVisitingSubject = new Subject<SelectedDirectory>();
		_folderLeftSubject = new Subject<SelectedDirectory>();

		_driveModelsFactory = new DriveModelsFactory();
	}

	/// <inheritdoc />
	public IObservable<SelectedDirectory> FileSystemItemSelected => _selectedDirectorySubject.AsObservable();

	/// <inheritdoc />
	public IObservable<SelectedDirectory> FolderVisited => _folderVisitingSubject.AsObservable();

	/// <inheritdoc />
	public IObservable<SelectedDirectory> FolderLeft => _folderLeftSubject.AsObservable();

	/// <inheritdoc />
	public void Dispose()
	{
		_selectedDirectorySubject.Dispose();
		_folderVisitingSubject.Dispose();
		_folderLeftSubject.Dispose();
	}

	/// <inheritdoc />
	public async Task<DirectoryModel> GetDirectoryModelAsync(DirectoryModel? directoryModel = null)
	{
		if (directoryModel == null)
		{
			return await GetRootDirectoriesLevelAsync();
		}

		return await GetCustomDirectoriesLevelAsync(directoryModel);
	}

	/// <inheritdoc />
	public async Task<DirectoryModel> GetDirectoryModelAsync(string directoryPath)
	{
		return await Task.Run(
			       async () =>
			       {
				       var directoryInfo = new DirectoryInfo(directoryPath);
				       if (!directoryInfo.Exists)
				       {
					       return new DirectoryModel("Invalid folder", string.Empty);
				       }

				       var directoryModel = new DirectoryModel(directoryInfo.Name, directoryInfo.FullName);

				       return await GetCustomDirectoriesLevelAsync(directoryModel);
			       });
	}

	/// <inheritdoc />
	public async Task<IEnumerable<FileModel>> GetFileModelAsync(DirectoryModel directoryModel, string searchPattern)
	{
		return await Task.Run(async () => await GetFileModelAsync(directoryModel.Path, searchPattern));
	}

	public async Task<IEnumerable<FileModel>> GetFileModelAsync(string directoryPath, string searchPattern)
	{
		return await Task.Run(
			       () =>
			       {
				       var files = new List<FileModel>();
				       var directoryInfo = new DirectoryInfo(directoryPath);

				       if (!directoryInfo.Exists)
				       {
					       return files;
				       }

				       files.AddRange(directoryInfo.EnumerateFiles(searchPattern).Select(fileInfo => new FileModel(fileInfo.Name, fileInfo.FullName)));

				       return files;
			       });
	}

	/// <inheritdoc />
	public void SetSelectedDirectory(SelectedDirectory selectedDirectory)
	{
		_selectedDirectories.AddOrUpdate(selectedDirectory.FileManagerPanel, _ => selectedDirectory, (_, _) => selectedDirectory);
		_selectedDirectorySubject.OnNext(selectedDirectory);
	}

	/// <inheritdoc />
	public DirectoryModel? GetSelectedDirectory(FileManagerPanel fileManagerPanel)
	{
		return _selectedDirectories.GetValueOrDefault(fileManagerPanel);
	}

	/// <inheritdoc />
	public void AddVisitingFolder(DirectoryModel directoryModel, FileManagerPanel fileManagerPanel)
	{
		var visitingDirectory = new SelectedDirectory(directoryModel, fileManagerPanel);
		if (_visitingDirectoryModels.TryAdd((directoryModel.Path, fileManagerPanel), visitingDirectory))
		{
			_folderVisitingSubject.OnNext(visitingDirectory);
		}
	}

	/// <inheritdoc />
	public void RemoveVisitingFolder(DirectoryModel directoryModel, FileManagerPanel fileManagerPanel)
	{
		if (_visitingDirectoryModels.TryRemove((directoryModel.Path, fileManagerPanel), out var removedDirectoryModel))
		{
			_folderLeftSubject.OnNext(removedDirectoryModel);
		}
	}

	/// <inheritdoc />
	public void RemoveFolder(DirectoryModel directoryModel)
	{
		if (directoryModel is DriveModel || directoryModel is DeviceModel)
		{
			return;
		}

		if (Directory.EnumerateFiles(directoryModel.Path).Any())
		{
			return;
		}

		FileSystem.DeleteDirectory(directoryModel.Path, UIOption.AllDialogs, RecycleOption.SendToRecycleBin);
	}

	/// <inheritdoc />
	public DirectoryModel? CreateSubFolder(DirectoryModel directoryModel)
	{
		if (directoryModel is DeviceModel)
		{
			return null;
		}

		var fullName = CreateNewDirectoryFullName(directoryModel.Path);
		FileSystem.CreateDirectory(fullName);
		var directoryInfo = new DirectoryInfo(fullName);

		return new DirectoryModel(directoryInfo.Name, fullName);
	}

	/// <inheritdoc />
	public string? RenameFolder(string? newName, string path)
	{
		var directoryInfo = new DirectoryInfo(path);

		if (string.IsNullOrWhiteSpace(newName))
		{
			return directoryInfo.Name;
		}

		if (!directoryInfo.Exists)
		{
			return directoryInfo.Name;
		}

		if (Directory.Exists(Path.Combine(directoryInfo.Parent.FullName, newName)))
		{
			return directoryInfo.Name;
		}

		FileSystem.RenameDirectory(path, newName);

		return newName;
	}

	/// <inheritdoc />
	public async Task<FolderStatistics> GetFolderStatisticsAsync(string folderPath)
	{
		var tasks = new List<Task<(MediaFormat, long)>>();

		foreach (var extension in MediaFormat.GetSupportedExtensions())
		{
			var task = Task.Factory.StartNew(() => GetMediaFormatCountFromFolder(folderPath, extension));
			tasks.Add(task);
		}

		var results = await Task.WhenAll(tasks);
		var statistics = new FolderStatistics();

		foreach (var taskResult in results)
		{
			statistics.AddMediaFormatStatistics(taskResult.Item1, taskResult.Item2);
		}

		return statistics;
	}

	public async Task<DirectoryModel> GetCustomDirectoriesLevelAsync(DirectoryModel directoryModel, bool preview = false)
	{
		return await Task.Run(
			       () =>
			       {
				       var rootDirectoryInfo = new DirectoryInfo(directoryModel.Path);
				       if (!rootDirectoryInfo.Exists || directoryModel.DirectoryModels.Any())
				       {
					       return directoryModel;
				       }

				       foreach (var directoryInfo in rootDirectoryInfo.EnumerateDirectories())
				       {
					       var directory = new DirectoryModel(directoryInfo.Name, directoryInfo.FullName);

					       // Add first subdirectory if possible.
					       var directoryModelInfo = new DirectoryInfo(directory.Path);

					       try
					       {
						       if (directoryModelInfo.EnumerateDirectories().FirstOrDefault() is { } subDirectoryInfo)
						       {
							       directory.AddDirectory(new DirectoryModel(subDirectoryInfo.Name, subDirectoryInfo.FullName));
						       }

						       foreach (var extension in MediaFormat.GetSupportedExtensions())
						       {
							       if (directoryModelInfo.EnumerateFiles($"*{extension}", SearchOption.TopDirectoryOnly).Any())
							       {
								       directory.HasSupportedMedia = true;
								       break;
							       }
						       }

						       directoryModel.AddDirectory(directory);
					       }
					       catch (UnauthorizedAccessException)
					       {
						       // Ignored.
					       }

					       if (preview)
					       {
						       break;
					       }
				       }

				       return directoryModel;
			       });
	}

	private (MediaFormat, long) GetMediaFormatCountFromFolder(string folderPath, string extension)
	{
		var mediaFormat = MediaFormat.GetMediaFormatByExtension(extension);
		if (mediaFormat != null)
		{
			var files = Directory.GetFiles(folderPath, $"*{extension}", SearchOption.TopDirectoryOnly);

			return (mediaFormat, files.Length);
		}

		return (MediaFormat.MediaFormatUnknown, 0);
	}

	private string CreateNewDirectoryFullName(string directoryModelPath)
	{
		const string initialName = "New Folder";
		var counter = 0;
		var finalName = initialName;

		while (Directory.EnumerateDirectories(directoryModelPath, finalName, SearchOption.TopDirectoryOnly).Any())
		{
			finalName = $"{initialName}({counter})";
			counter++;
		}

		return Path.Combine(directoryModelPath, finalName);
	}

	private async Task<DirectoryModel> GetRootDirectoriesLevelAsync()
	{
		return await Task.Run(
			       async () =>
			       {
				       var rootModel = new DeviceModel(Environment.MachineName, "//");
				       var drives = DriveInfo.GetDrives();

				       foreach (var driveInfo in drives)
				       {
					       if (driveInfo is { DriveType: DriveType.Network, IsReady: false })
					       {
						       continue;
					       }

					       if (_driveModelsFactory.CreateDriveModel(driveInfo) is { } drive)
					       {
						       rootModel.AddDirectory(drive);
						       if (drive.RootDirectory == null)
						       {
							       continue;
						       }

						       var firstDirectoryTier = await GetCustomDirectoriesLevelAsync(drive.RootDirectory, true);
						       drive.AddDirectories(firstDirectoryTier.DirectoryModels);
					       }
				       }

				       return rootModel;
			       });
	}
}
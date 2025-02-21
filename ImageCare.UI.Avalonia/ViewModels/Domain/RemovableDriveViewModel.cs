using System;
using System.Collections.Generic;

using AutoMapper;

using ImageCare.Core.Services.FolderService;

using Serilog;

namespace ImageCare.UI.Avalonia.ViewModels.Domain;

internal sealed class RemovableDriveViewModel : DriveViewModel
{
	private long _availableFreeSpace;
	private double _spacePercentage;
	private string _spaceInfo;

	/// <inheritdoc />
	public RemovableDriveViewModel(string? name,
	                               string path,
	                               long totalSize,
	                               long availableFreeSpace,
	                               IEnumerable<DirectoryViewModel> children,
	                               IFolderService folderService,
	                               IMapper mapper,
	                               ILogger logger)
		: base(name, path, children, folderService, mapper, logger)
	{
		TotalSize = totalSize;
		AvailableFreeSpace = availableFreeSpace;
	}

	public long TotalSize { get; }

	public long AvailableFreeSpace
	{
		get => _availableFreeSpace;
		set
		{
			SetProperty(ref _availableFreeSpace, value);
			CalculateSpacePercentage();
		}
	}

	public double SpacePercentage
	{
		get => _spacePercentage;
		set => SetProperty(ref _spacePercentage, value);
	}

	public string SpaceInfo
	{
		get => _spaceInfo;
		set => SetProperty(ref _spaceInfo, value);
	}

	private void CalculateSpacePercentage()
	{
		if (TotalSize != 0)
		{
			SpacePercentage = 100.0 - (double)AvailableFreeSpace / TotalSize * 100;
		}

		SpaceInfo = $"{Math.Round(AvailableFreeSpace / Math.Pow(1024, 3), 1)} gb \\ {Math.Round(TotalSize / Math.Pow(1024, 3), 1)} gb";
	}
}
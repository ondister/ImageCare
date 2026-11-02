using System;
using System.IO;
using System.Linq;

using ImageCare.Core.Domain.Folders;
using ImageCare.Core.Services.FileSystemService;
using ImageCare.Core.Services.FolderService;
using ImageCare.UI.Avalonia.ViewModels.Domain;

using Serilog;

namespace ImageCare.UI.Avalonia.ViewModels.Folders;

internal sealed class FolderMutationCoordinator
{
    private readonly IFolderService _folderService;
    private readonly IFileSystemService _fileSystemService;
    private readonly ILogger _logger;

    public FolderMutationCoordinator(
        IFolderService folderService,
        IFileSystemService fileSystemService,
        ILogger logger)
    {
        _folderService = folderService ?? throw new ArgumentNullException(nameof(folderService));
        _fileSystemService = fileSystemService ?? throw new ArgumentNullException(nameof(fileSystemService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void Rename(DirectoryViewModel directory)
    {
        ArgumentNullException.ThrowIfNull(directory);

        try
        {
            var requestedName = directory.EditableName?.Trim();
            if (string.IsNullOrWhiteSpace(requestedName))
            {
                directory.ResetEditableName();
                return;
            }

            if (string.Equals(requestedName, directory.Name, StringComparison.Ordinal))
            {
                return;
            }

            var oldDirectory = directory.ToDirectoryModel();
            var newName = _fileSystemService.RenameFolder(requestedName, oldDirectory.Path);
            if (string.IsNullOrEmpty(newName) || string.Equals(newName, directory.Name, StringComparison.Ordinal))
            {
                directory.ResetEditableName();
                return;
            }

            var parentPath = Path.GetDirectoryName(oldDirectory.Path)
                             ?? throw new InvalidOperationException($"Cannot rename root directory '{oldDirectory.Path}'");
            var renamedDirectory = new DirectoryModel(newName, Path.Combine(parentPath, newName));

            var expandedDirectories = directory.EnumerateSubtree()
                                               .Where(item => item.IsExpanded)
                                               .Select(item => item.ToDirectoryModel())
                                               .ToArray();
            foreach (var expandedDirectory in expandedDirectories)
            {
                _folderService.RemoveVisitingFolder(expandedDirectory, directory.FileManagerPanel);
            }

            directory.UpdateDirectory(renamedDirectory);
            directory.RefreshParentSort();

            foreach (var expandedDirectory in directory.EnumerateSubtree().Where(item => item.IsExpanded))
            {
                _folderService.AddVisitingFolder(
                    expandedDirectory.ToDirectoryModel(),
                    directory.FileManagerPanel);
            }

            if (directory.IsSelected)
            {
                _folderService.SetSelectedDirectory(new SelectedDirectory(renamedDirectory, directory.FileManagerPanel));
            }
        }
        catch (Exception ex)
        {
            _logger.Error(
                ex,
                "Failed to rename folder from {OldName} to {NewName}",
                directory.Name,
                directory.EditableName);
            directory.ResetEditableName();
        }
    }

    public void Create(DirectoryViewModel parent)
    {
        ArgumentNullException.ThrowIfNull(parent);

        try
        {
            var createdSubFolder = _folderService.CreateSubFolder(parent.ToDirectoryModel());
            if (createdSubFolder == null)
            {
                return;
            }

            var createdViewModel = parent.ChildFileSystemItems.FirstOrDefault(child =>
                child.Path.Equals(createdSubFolder.Path, StringComparison.OrdinalIgnoreCase));
            if (createdViewModel == null)
            {
                createdViewModel = parent.MapDirectory(createdSubFolder);
                parent.ChildFileSystemItems.Add(createdViewModel);
            }

            createdViewModel.IsEditing = true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to create folder for: {SelectedPath}", parent.Path);
        }
    }

    public void Delete(DirectoryViewModel directory)
    {
        ArgumentNullException.ThrowIfNull(directory);

        try
        {
            if (directory.HasSupportedMedia)
            {
                return;
            }

            var deletedDirectory = directory.ToDirectoryModel();
            _folderService.RemoveFolder(deletedDirectory);
            if (_fileSystemService.DirectoryExists(deletedDirectory.Path))
            {
                return;
            }

            if (directory.IsExpanded)
            {
                _folderService.RemoveVisitingFolder(deletedDirectory, directory.FileManagerPanel);
            }

            if (directory.IsSelected)
            {
                _folderService.SetSelectedDirectory(new SelectedDirectory(DirectoryModel.Empty, directory.FileManagerPanel));
            }

            directory.DetachFromParent();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to delete folder: {Path}", directory.Path);
        }
    }
}

using System.Collections.Generic;

using ImageCare.Core.Services;

namespace ImageCare.UI.Avalonia.ViewModels.Domain;

internal class DriveViewModel : DirectoryViewModel
{
    /// <inheritdoc />
    public DriveViewModel(string name, string path, IEnumerable<FileSystemItemViewModel> children, IFolderService folderService)
        : base(name, path, children, folderService) { }
}
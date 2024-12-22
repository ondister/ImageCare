using System.Collections.Generic;

using ImageCare.Core.Services;

using Serilog;

namespace ImageCare.UI.Avalonia.ViewModels.Domain;

internal sealed class RemovableDriveViewModel : DriveViewModel
{
    /// <inheritdoc />
    public RemovableDriveViewModel(string? name,
                                   string path,
                                   IEnumerable<FileSystemItemViewModel> children,
                                   IFolderService folderService,
                                   ILogger logger)
        : base(name, path, children, folderService, logger) { }
}
using System.Linq;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace ImageCare.UI.Avalonia.Services;

public sealed class AvaloniaFileDialogService : IFileDialogService
{
	public async Task<string?> ShowOpenFileDialogAsync(string title, params (string Name, string[] Patterns)[] filters)
	{
		var topLevel = GetTopLevel();
		if (topLevel == null)
		{
			return null;
		}

		var filePickerFilters = filters.Select(f => new FilePickerFileType(f.Name)
		                               {
			                               Patterns = f.Patterns
		                               })
		                               .ToArray();

		var files = await topLevel.StorageProvider.OpenFilePickerAsync(
			            new FilePickerOpenOptions
			            {
				            Title = title,
				            AllowMultiple = false,
				            FileTypeFilter = filePickerFilters
			            });

		return files.Count >= 1 ? files[0].Path.LocalPath : null;
	}

	private TopLevel? GetTopLevel()
	{
		if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
		{
			return desktop.MainWindow;
		}

		return TopLevel.GetTopLevel(null);
	}
}
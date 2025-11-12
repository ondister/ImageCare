using System.Threading.Tasks;

namespace ImageCare.UI.Avalonia.Services;

public interface IFileDialogService
{
	Task<string?> ShowOpenFileDialogAsync(string title, params (string Name, string[] Patterns)[] filters);
}
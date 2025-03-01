using System.Threading.Tasks;

using Avalonia.Controls;

namespace ImageCare.UI.Avalonia.Services;

internal sealed class ClipboardService : IClipboardService
{
	private readonly TopLevel _topLevel;

	public ClipboardService(TopLevel topLevel)
	{
		_topLevel = topLevel;
	}

	/// <inheritdoc />
	public async Task CopyToClipboardAsync(string text)
	{
		var clipboard = _topLevel.Clipboard;
		if (clipboard != null)
		{
			await clipboard.SetTextAsync(text);
		}
	}
}
using System.Threading.Tasks;

namespace ImageCare.UI.Avalonia.Services;

internal interface IClipboardService
{
	Task CopyToClipboardAsync(string text);
}
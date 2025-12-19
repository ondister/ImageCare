using System;

namespace ImageCare.UI.Avalonia.ViewModels.Domain.Logs;

internal sealed class WarningLogMessageViewModel : LogMessageViewModel
{
	/// <inheritdoc />
	public WarningLogMessageViewModel(DateTimeOffset timestamp, string message, string? exceptionMessage)
		: base(timestamp, message, exceptionMessage) { }
}
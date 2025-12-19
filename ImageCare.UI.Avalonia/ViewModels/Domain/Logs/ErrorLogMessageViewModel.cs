using System;

namespace ImageCare.UI.Avalonia.ViewModels.Domain.Logs;

internal sealed class ErrorLogMessageViewModel : LogMessageViewModel
{
	/// <inheritdoc />
	public ErrorLogMessageViewModel(DateTimeOffset timestamp, string message, string? exceptionMessage)
		: base(timestamp, message, exceptionMessage) { }
}
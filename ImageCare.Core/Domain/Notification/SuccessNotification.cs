namespace ImageCare.Core.Domain.Notification;

public sealed class SuccessNotification : Notification
{
	/// <inheritdoc />
	public SuccessNotification(string title, string? description)
		: base(title, description) { }
}
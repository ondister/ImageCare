namespace ImageCare.Core.Exceptions;

public class MediaPreviewProviderException : Exception
{
	public MediaPreviewProviderException(string message)
		: base(message) { }

	public MediaPreviewProviderException(string message, Exception innerException)
		: base(message, innerException) { }
}
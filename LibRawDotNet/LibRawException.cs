namespace LibRawDotNet;

public class LibRawException : Exception
{
    public LibRawException() { }

    public LibRawException(string? message)
        : base(message) { }

    public LibRawException(string? message, Exception? innerException)
        : base(message, innerException) { }
}
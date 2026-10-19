namespace ImageCare.Core.Services.FileSystemService;

public static class PortableFileNameValidator
{
    private static readonly char[] InvalidCharacters = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];

    private static readonly HashSet<string> WindowsReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    public static bool IsValid(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) ||
            fileName.Length > 255 ||
            fileName is "." or ".." ||
            fileName.EndsWith(' ') ||
            fileName.EndsWith('.') ||
            fileName.IndexOfAny(InvalidCharacters) >= 0 ||
            fileName.Any(character => char.IsControl(character)))
        {
            return false;
        }

        var baseName = fileName.Split('.')[0];
        return !WindowsReservedNames.Contains(baseName);
    }
}

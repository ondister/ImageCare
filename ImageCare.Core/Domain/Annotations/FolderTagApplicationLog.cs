using System.Globalization;
using System.Text;

namespace ImageCare.Core.Domain.Annotations;

/// <summary>
/// Append-only debug journal for folder-tag applications. Writes one file per day
/// (<c>tags-YYYYMMDD.log</c>) and keeps at most <see cref="RetentionDays"/> days.
/// </summary>
public sealed class FolderTagApplicationLog
{
    public const int RetentionDays = 3;
    private const string FileNamePrefix = "tags-";
    private const string FileNameExtension = ".log";

    private readonly string _directory;
    private readonly object _gate = new();

    public FolderTagApplicationLog(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = directory;
    }

    public void Append(string action, string mediaPath, string sourceDirectory, IReadOnlyList<string> addedTags)
    {
        if (addedTags.Count == 0)
        {
            return;
        }

        var timestamp = DateTimeOffset.Now;
        var line = string.Create(
            CultureInfo.InvariantCulture,
            $"{timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}\t{action}\t{mediaPath}\t{sourceDirectory}\t{string.Join(", ", addedTags)}");

        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(_directory);
                var path = Path.Combine(_directory, $"{FileNamePrefix}{timestamp:yyyyMMdd}{FileNameExtension}");
                File.AppendAllText(path, line + Environment.NewLine, new UTF8Encoding(false));
                PruneOldFiles(timestamp.DateTime);
            }
            catch
            {
                // Logging is best-effort and must never break the sort operation.
            }
        }
    }

    private void PruneOldFiles(DateTime now)
    {
        try
        {
            var threshold = now.Date.AddDays(-(RetentionDays - 1));
            foreach (var path in Directory.EnumerateFiles(_directory, $"{FileNamePrefix}*{FileNameExtension}"))
            {
                var name = Path.GetFileNameWithoutExtension(path);
                var stamp = name.Length > FileNamePrefix.Length ? name[FileNamePrefix.Length..] : string.Empty;
                if (DateTime.TryParseExact(
                        stamp,
                        "yyyyMMdd",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out var fileDate)
                    && fileDate.Date < threshold)
                {
                    File.Delete(path);
                }
            }
        }
        catch
        {
            // Pruning is best-effort.
        }
    }
}

using System.Text.Json;
using System.Text.Json.Serialization;

using ImageCare.Mcp;

using Serilog;

namespace ImageCare.Mcp.Sessions;

/// <summary>
/// Persists review sessions as individual JSON documents so they survive application
/// restarts and remain inspectable by a human.
/// </summary>
public sealed class ReviewSessionStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _directory;
    private readonly ILogger _logger;

    public ReviewSessionStore(ImageCareMcpOptions options, ILogger logger)
    {
        _logger = logger;
        _directory = Path.Combine(options.DataDirectory, "sessions");
        Directory.CreateDirectory(_directory);
    }

    public string DirectoryPath => _directory;

    public IReadOnlyList<string> ListSessionIds()
    {
        try
        {
            return Directory.EnumerateFiles(_directory, "session-*.json")
                .Select(Path.GetFileNameWithoutExtension)
                .Where(name => name is not null)
                .Select(name => name!["session-".Length..])
                .OrderByDescending(id => id, StringComparer.Ordinal)
                .ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.Warning(exception, "Failed to list MCP review sessions in {Directory}", _directory);
            return [];
        }
    }

    public ReviewSession? TryLoad(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || !IsSafeId(sessionId))
        {
            return null;
        }

        var path = GetSessionPath(sessionId);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize<ReviewSession>(stream, SerializerOptions);
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            _logger.Warning(exception, "Failed to read MCP review session {SessionId}", sessionId);
            return null;
        }
    }

    public ReviewSession? TryLoadLatest()
    {
        foreach (var id in ListSessionIds())
        {
            var session = TryLoad(id);
            if (session is not null)
            {
                return session;
            }
        }

        return null;
    }

    public void Save(ReviewSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        Directory.CreateDirectory(_directory);
        var path = GetSessionPath(session.Id);
        var temporaryPath = path + ".tmp";

        try
        {
            session.UpdatedUtc = DateTime.UtcNow;
            using (var stream = File.Create(temporaryPath))
            {
                JsonSerializer.Serialize(stream, session, SerializerOptions);
            }

            File.Move(temporaryPath, path, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.Error(exception, "Failed to save MCP review session {SessionId}", session.Id);
            TryDelete(temporaryPath);
            throw;
        }
    }

    public bool Exists(string sessionId) => IsSafeId(sessionId) && File.Exists(GetSessionPath(sessionId));

    private string GetSessionPath(string sessionId) =>
        Path.Combine(_directory, $"session-{sessionId}.json");

    private static bool IsSafeId(string id) =>
        id.Length is > 0 and <= 64 && id.All(character => char.IsLetterOrDigit(character) || character is '-');

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Best-effort cleanup of a temporary file.
        }
    }
}

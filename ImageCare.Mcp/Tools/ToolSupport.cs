using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

using ImageCare.Core.Domain.MediaFormats;

using ImageCare.Mcp.Media;
using ImageCare.Mcp.Security;
using ImageCare.Mcp.Sessions;

using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace ImageCare.Mcp.Tools;

/// <summary>Shared serialization and content-block helpers for the MCP tools.</summary>
internal static class ToolSupport
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,

        // Keep Cyrillic tags and paths readable in tool output instead of \uXXXX escapes.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string Json(object value) => JsonSerializer.Serialize(value, SerializerOptions);

    public static IReadOnlyList<ContentBlock> WithImages(string summary, IEnumerable<McpImage> images)
    {
        var blocks = new List<ContentBlock> { new TextContentBlock { Text = summary } };
        foreach (var image in images)
        {
            blocks.Add(new TextContentBlock { Text = $"Image: {image.Path}" });
            blocks.Add(ImageContentBlock.FromBytes(image.Bytes, image.MimeType));
        }

        return blocks;
    }

    public static MediaFormat ResolveFormat(string path) => ReviewSessionService.ResolveFormat(path);

    public static string RequireAllowedPath(this McpPathGuard guard, string? path, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new McpException($"{parameterName} is required.");
        }

        return guard.Require(path, parameterName);
    }
}

/// <summary>
/// Validates that a path is inside a location the user made available to the assistant.
/// </summary>
public sealed class McpPathGuard
{
    private readonly McpPathPolicy _policy;
    private readonly ReviewSessionService _sessionService;

    public McpPathGuard(McpPathPolicy policy, ReviewSessionService sessionService)
    {
        _policy = policy;
        _sessionService = sessionService;
    }

    public string Require(string path, string parameterName)
    {
        if (!Path.IsPathFullyQualified(path))
        {
            throw new McpException(
                $"{parameterName} must be an absolute path. Received '{path}'.");
        }

        var roots = _sessionService.GetSessionRoots();
        if (!_policy.IsAllowed(path, roots))
        {
            throw new McpException(
                $"{parameterName} '{path}' is outside the folders exposed to the assistant. " +
                "Add it under Settings > Search roots, use a removable drive, or start a session for its parent folder.");
        }

        return Path.GetFullPath(path);
    }
}

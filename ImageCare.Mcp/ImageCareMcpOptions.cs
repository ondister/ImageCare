namespace ImageCare.Mcp;

/// <summary>
/// Configuration for the embedded MCP server. The server only listens on the loopback
/// interface by default; expose it to other hosts only deliberately.
/// </summary>
public sealed class ImageCareMcpOptions
{
    public const string SectionName = "ImageCare";

    /// <summary>TCP port for the loopback HTTP endpoint.</summary>
    public int Port { get; init; } = 5277;

    /// <summary>Interface to bind. Keep this on loopback for local integrations.</summary>
    public string ListenHost { get; init; } = "127.0.0.1";

    /// <summary>
    /// Optional bearer token. When set, every MCP and REST request must present
    /// <c>Authorization: Bearer &lt;token&gt;</c>. This protects the local endpoint from
    /// other processes on the same machine.
    /// </summary>
    public string? AccessToken { get; init; }

    /// <summary>Directory for persisted review sessions and caches.</summary>
    public string DataDirectory { get; init; } = GetDefaultDataDirectory();

    /// <summary>Maximum number of files a single delete call may remove without an explicit override.</summary>
    public int MaxFilesPerDelete { get; init; } = 200;

    /// <summary>Longest edge, in pixels, for previews returned to models.</summary>
    public int PreviewMaxEdge { get; init; } = 1024;

    /// <summary>JPEG quality used for generated previews.</summary>
    public int PreviewQuality { get; init; } = 82;

    /// <summary>Default number of images returned by a single preview-bearing call.</summary>
    public int DefaultPreviewCount { get; init; } = 8;

    /// <summary>Upper bound for images returned by a single call.</summary>
    public int MaxPreviewCount { get; init; } = 24;

    public static string GetDefaultDataDirectory()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ImageCare",
            "Mcp");
    }
}

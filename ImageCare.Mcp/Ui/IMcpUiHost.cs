namespace ImageCare.Mcp.Ui;

public enum McpPanelSide
{
    Left,
    Right
}

public sealed record McpUiFile(
    string Path,
    string? Name,
    int Rating,
    IReadOnlyList<string> Tags);

/// <summary>
/// Snapshot of what the user is currently looking at inside the desktop application.
/// </summary>
public sealed record McpUiState(
    bool IsAvailable,
    string? SelectedPanel,
    string? LeftFolderPath,
    string? RightFolderPath,
    string? FocusedFilePath,
    IReadOnlyList<McpUiFile> VisibleFiles);

public sealed record McpUiOpenResult(
    bool Success,
    string? Message,
    string? FolderPath);

/// <summary>
/// Bridge from MCP tools into the live desktop shell. An empty implementation is used when
/// the server runs headless, so tools that need the UI degrade gracefully instead of failing.
/// </summary>
public interface IMcpUiHost
{
    bool IsAvailable { get; }

    Task<McpUiOpenResult> OpenFolderAsync(
        string folderPath,
        McpPanelSide side,
        CancellationToken cancellationToken = default);

    Task<McpUiState> GetStateAsync(CancellationToken cancellationToken = default);
}

public sealed class NullMcpUiHost : IMcpUiHost
{
    public static NullMcpUiHost Instance { get; } = new();

    public bool IsAvailable => false;

    public Task<McpUiOpenResult> OpenFolderAsync(
        string folderPath,
        McpPanelSide side,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new McpUiOpenResult(
            false,
            "ImageCare UI bridge is not available. The MCP server is running headless.",
            null));
    }

    public Task<McpUiState> GetStateAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new McpUiState(false, null, null, null, null, []));
    }
}

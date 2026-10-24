using System.ComponentModel;

using ImageCare.Mcp.Ui;

using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace ImageCare.Mcp.Tools;

[McpServerToolType]
public sealed class UiTools(IMcpUiHost uiHost, McpPathGuard pathGuard)
{
    [McpServerTool(
        Name = "imagecare_open_folder",
        Title = "Open a folder in ImageCare",
        ReadOnly = false,
        Idempotent = true,
        Destructive = false)]
    [Description(
        "Opens a folder in the ImageCare desktop window (left or right panel) so the user can " +
        "cull the same files the assistant is looking at. Requires the embedded server.")]
    public async Task<string> OpenFolder(
        [Description("Absolute path to an allowed directory.")]
        string folderPath,
        [Description("Panel to use: 'left' or 'right'.")]
        string panel = "left",
        CancellationToken cancellationToken = default)
    {
        var path = pathGuard.Require(folderPath, nameof(folderPath));
        if (!Directory.Exists(path))
        {
            throw new McpException($"Directory not found: {path}");
        }

        var side = ParsePanel(panel);
        var result = await uiHost.OpenFolderAsync(path, side, cancellationToken).ConfigureAwait(false);

        return ToolSupport.Json(new
        {
            requested = new { folderPath = path, panel = side.ToString().ToLowerInvariant() },
            result.Success,
            result.Message
        });
    }

    [McpServerTool(
        Name = "imagecare_ui_state",
        Title = "Read the UI state",
        ReadOnly = true,
        Idempotent = true,
        Destructive = false)]
    [Description(
        "Returns which folders are open in the desktop window and which files are visible, so " +
        "the assistant can align with what the user is currently doing.")]
    public async Task<string> UiState(CancellationToken cancellationToken = default)
    {
        var state = await uiHost.GetStateAsync(cancellationToken).ConfigureAwait(false);
        return ToolSupport.Json(new
        {
            state.IsAvailable,
            state.SelectedPanel,
            state.LeftFolderPath,
            state.RightFolderPath,
            state.FocusedFilePath,
            visibleFiles = state.VisibleFiles.Select(file => new
            {
                file.Path,
                file.Name,
                file.Rating,
                file.Tags
            })
        });
    }

    private static McpPanelSide ParsePanel(string? panel)
    {
        if (string.IsNullOrWhiteSpace(panel))
        {
            return McpPanelSide.Left;
        }

        return panel.Trim().ToLowerInvariant() switch
        {
            "left" => McpPanelSide.Left,
            "right" => McpPanelSide.Right,
            _ => throw new McpException("panel must be 'left' or 'right'.")
        };
    }
}

using System.Security.Cryptography;
using System.Text;

using ImageCare.Core.Domain.Annotations;
using ImageCare.Core.Services.ConfigurationService;
using ImageCare.Core.Services.FileSystemService;
using ImageCare.Core.Services.MediaAnnotationsService;
using ImageCare.Core.Services.MediaPreviewOperationsService;
using ImageCare.Core.Services.MediaPreviewService;

using ImageCare.Mcp.Media;
using ImageCare.Mcp.Security;
using ImageCare.Mcp.Sessions;
using ImageCare.Mcp.Tags;
using ImageCare.Mcp.Tools;
using ImageCare.Mcp.Ui;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using ModelContextProtocol;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

using Serilog;

using SerilogLogger = Serilog.ILogger;

namespace ImageCare.Mcp;

/// <summary>
/// Core services the MCP host borrows from the desktop application. Passing resolved instances
/// (instead of a DI container) keeps the host independent from the Prism/DryIoc container and
/// guarantees that tools act on the same in-memory state the UI shows.
/// </summary>
public sealed class ImageCareMcpServices
{
    public required IFileSystemService FileSystemService { get; init; }

    public required IMediaPreviewService MediaPreviewService { get; init; }

    public required IMediaAnnotationsService AnnotationsService { get; init; }

    public required IMediaPreviewOperationsService PreviewOperations { get; init; }

    public required IFolderTagApplicationService FolderTagApplicationService { get; init; }

    public required IFolderTagsStore FolderTagsStore { get; init; }

    public required IConfigurationService ConfigurationService { get; init; }

    public required SerilogLogger Logger { get; init; }

    public IMcpUiHost? UiHost { get; init; }
}

/// <summary>
/// Embedded ASP.NET Core minimal-API host that exposes the Model Context Protocol over
/// Streamable HTTP. It is started in-process by the ImageCare desktop application, so tools
/// reach live services and the open window instead of talking to a second process.
/// </summary>
public sealed class ImageCareMcpHost : IAsyncDisposable
{
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private const string ServerInstructions =
        """
        ImageCare is a photo-culling desktop application. The user reviews photos of a memory card
        in batches, keeping the good ones by rating them (1-5 stars) and/or tagging them, and moving
        keepers into library folders.

        Standard culling loop:
        1. imagecare_list_drives -> find the card. imagecare_list_folders -> find the DCIM folder.
        2. imagecare_session_create(sourcePath) -> batches are ordered by capture time and grouped
           by shooting cluster, so one burst of frames stays together.
        3. imagecare_session_get(includePreviews=true) -> look at the images if you are multimodal.
           Offer a short, concrete observation (species, scene, technical flaws) and, when useful,
           tag suggestions from imagecare_tags_suggest. Do not decide for the user; they cull.
        4. The user rates/tags/moves photos in ImageCare. Then imagecare_session_sync reads their
           decisions. A file is "kept" when it has any tag or a rating >= keepRating.
        5. Only after the user explicitly says "delete the rest" call
           imagecare_session_delete_remaining (dryRun defaults to true: show the list first).
        6. imagecare_session_next -> next batch. imagecare_session_stats -> running statistics.

        Rules:
        - Never delete or overwrite anything without an explicit user instruction.
        - Always dry-run destructive tools first and report counts.
        - Tag suggestions are evidence-based: explain which kept photo or folder-tags.txt entry the
          suggestion comes from, and ask before applying.
        - Use imagecare_rating_set to change a star rating (0 clears it) only when the user explicitly
          asks; dryRun defaults to true.
        - Paths always come from the user or from tool output; do not guess them.
        """;

    private WebApplication? _app;
    private readonly ImageCareMcpOptions _options;
    private readonly SerilogLogger _logger;

    private ImageCareMcpHost(ImageCareMcpOptions options, SerilogLogger logger)
    {
        _options = options;
        _logger = logger;
    }

    public bool IsRunning => _app is not null;

    public string? EndpointUrl { get; private set; }

    public static async Task<ImageCareMcpHost> StartAsync(
        ImageCareMcpServices services,
        ImageCareMcpOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        var host = new ImageCareMcpHost(options, services.Logger);
        await host.StartCoreAsync(services, cancellationToken).ConfigureAwait(false);
        return host;
    }

    private async Task StartCoreAsync(ImageCareMcpServices services, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_options.DataDirectory);

        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(ImageCareMcpHost).Assembly.GetName().Name,
            ContentRootPath = AppContext.BaseDirectory,
            Args = []
        });

        builder.Logging.ClearProviders();
        builder.Configuration["AllowedHosts"] = "127.0.0.1;localhost;[::1]";

        builder.WebHost.UseUrls($"http://{_options.ListenHost}:{_options.Port}");
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.AddServerHeader = false);

        // Reject requests whose Host header is not loopback: this blocks DNS-rebinding attempts
        // from a web page the user might have open in a browser.
        builder.Services.AddHostFiltering(options =>
        {
            options.AllowedHosts = ["127.0.0.1", "localhost", "[::1]"];
        });

        builder.Services.AddSingleton(_options);
        builder.Services.AddSingleton(services.Logger);
        builder.Services.AddSingleton(services.FileSystemService);
        builder.Services.AddSingleton(services.MediaPreviewService);
        builder.Services.AddSingleton(services.AnnotationsService);
        builder.Services.AddSingleton(services.PreviewOperations);
        builder.Services.AddSingleton(services.FolderTagApplicationService);
        builder.Services.AddSingleton(services.FolderTagsStore);
        builder.Services.AddSingleton(services.ConfigurationService);
        builder.Services.AddSingleton(services.UiHost ?? NullMcpUiHost.Instance);

        builder.Services.AddSingleton<MediaLibrary>();
        builder.Services.AddSingleton<ThumbnailService>();
        builder.Services.AddSingleton<ReviewSessionStore>();
        builder.Services.AddSingleton<ReviewSessionService>();
        builder.Services.AddSingleton<McpPathPolicy>();
        builder.Services.AddSingleton<McpPathGuard>();
        builder.Services.AddSingleton<TagSuggestionService>();

        builder.Services
            .AddMcpServer(mcp =>
            {
                mcp.ServerInfo = new Implementation
                {
                    Name = "ImageCare",
                    Version = typeof(ImageCareMcpHost).Assembly.GetName().Version?.ToString() ?? "1.0.0"
                };
                mcp.ServerInstructions = ServerInstructions;
            })
            .WithHttpTransport(transport => transport.SessionMode = HttpServerSessionMode.Stateless)
            .WithToolsFromAssembly(typeof(ImageCareMcpHost).Assembly);

        var app = builder.Build();

        app.UseHostFiltering();
        app.Use(async (context, next) =>
        {
            if (!IsAuthorized(context))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.Headers.WWWAuthenticate = "Bearer";
                return;
            }

            await next(context).ConfigureAwait(false);
        });

        MapRestEndpoints(app);
        app.MapMcp("/mcp");

        await app.StartAsync(cancellationToken).ConfigureAwait(false);
        _app = app;

        EndpointUrl = ResolveEndpoint(app);
        _logger.Information(
            "ImageCare MCP server listening on {Endpoint} (stateless Streamable HTTP, {Auth})",
            EndpointUrl,
            string.IsNullOrEmpty(_options.AccessToken) ? "no token" : "bearer token required");
    }

    private void MapRestEndpoints(WebApplication app)
    {
        app.MapGet("/health", () => Results.Json(new
        {
            status = "ok",
            server = "ImageCare.Mcp",
            version = typeof(ImageCareMcpHost).Assembly.GetName().Version?.ToString(),
            transport = "streamable-http",
            endpoint = "/mcp",
            authRequired = !string.IsNullOrEmpty(_options.AccessToken)
        }));

        app.MapGet("/api/drives", () =>
        {
            var drives = DriveInfo.GetDrives().Select(drive =>
            {
                bool ready;
                long total = 0;
                long free = 0;
                try
                {
                    ready = drive.IsReady;
                    if (ready)
                    {
                        total = drive.TotalSize;
                        free = drive.AvailableFreeSpace;
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    ready = false;
                }

                return new
                {
                    name = drive.Name,
                    root = drive.RootDirectory.FullName,
                    type = drive.DriveType.ToString(),
                    ready,
                    total,
                    free
                };
            });

            return Results.Json(new { drives });
        });

        app.MapGet("/api/media/preview", async (
            string path,
            int? maxEdge,
            ThumbnailService thumbnails,
            McpPathGuard guard,
            CancellationToken cancellationToken) =>
        {
            string fullPath;
            try
            {
                fullPath = guard.Require(path, nameof(path));
            }
            catch (McpException exception)
            {
                return Results.Problem(exception.Message, statusCode: StatusCodes.Status403Forbidden);
            }

            var image = await thumbnails.CreateAsync(fullPath, maxEdge, cancellationToken)
                .ConfigureAwait(false);
            return image is null
                ? Results.NotFound()
                : Results.File(image.Bytes, image.MimeType);
        });

        app.MapGet("/api/media/metadata", async (
            string path,
            ThumbnailService thumbnails,
            IMediaPreviewService previews,
            IMediaAnnotationsService annotations,
            McpPathGuard guard,
            CancellationToken cancellationToken) =>
        {
            _ = thumbnails;
            string fullPath;
            try
            {
                fullPath = guard.Require(path, nameof(path));
            }
            catch (McpException exception)
            {
                return Results.Problem(exception.Message, statusCode: StatusCodes.Status403Forbidden);
            }

            if (!File.Exists(fullPath))
            {
                return Results.NotFound();
            }

            var info = new FileInfo(fullPath);
            var annotation = await annotations.GetAsync(fullPath, cancellationToken).ConfigureAwait(false);
            object? metadata = null;
            try
            {
                var preview = await previews.GetMediaPreviewAsync(fullPath, cancellationToken)
                    .ConfigureAwait(false);
                if (preview is not null)
                {
                    var value = await previews.GetMediaMetadataAsync(preview, cancellationToken)
                        .ConfigureAwait(false);
                    metadata = new
                    {
                        creationDateTime = value.CreationDateTime,
                        width = value.Width,
                        height = value.Height,
                        values = value.AllMetadata
                    };
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                metadata = null;
            }

            return Results.Json(new
            {
                path = fullPath,
                name = info.Name,
                length = info.Length,
                createdUtc = info.CreationTimeUtc,
                modifiedUtc = info.LastWriteTimeUtc,
                rating = annotation.Rating,
                tags = annotation.Tags,
                metadata
            });
        });

        app.MapGet("/api/sessions", (ReviewSessionStore store) =>
        {
            var sessions = store.ListSessionIds()
                .Select(id => store.TryLoad(id))
                .Where(session => session is not null)
                .Select(session => new
                {
                    id = session!.Id,
                    name = session.Name,
                    sourcePath = session.SourcePath,
                    totalFiles = session.TotalFiles,
                    batches = session.Batches.Count,
                    currentBatchIndex = session.CurrentBatchIndex,
                    createdUtc = session.CreatedUtc,
                    updatedUtc = session.UpdatedUtc,
                    deletedByAgent = session.DeletedByAgent
                });

            return Results.Json(new { sessions });
        });
    }

    private bool IsAuthorized(HttpContext context)
    {
        if (string.IsNullOrEmpty(_options.AccessToken))
        {
            return true;
        }

        if (PathComparer.Equals(context.Request.Path, "/health"))
        {
            return true;
        }

        var header = context.Request.Headers.Authorization.ToString();
        const string prefix = "Bearer ";
        if (!header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var provided = header[prefix.Length..].Trim();
        return FixedTimeEquals(provided, _options.AccessToken);
    }

    private static bool FixedTimeEquals(string left, string right)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);
        return leftBytes.Length == rightBytes.Length
               && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }

    private static string ResolveEndpoint(WebApplication app)
    {
        var addresses = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()?.Addresses;

        var address = addresses?.FirstOrDefault();
        return address is null ? "http://127.0.0.1" : $"{address.TrimEnd('/')}/mcp";
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is null)
        {
            return;
        }

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await _app.StopAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException)
        {
            _logger.Warning(exception, "MCP server shutdown did not complete cleanly");
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "MCP server shutdown failed");
        }

        try
        {
            await _app.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is ObjectDisposedException or InvalidOperationException)
        {
            // The host can already be disposed while a stop attempt timed out; nothing to clean up.
        }

        _app = null;
        _logger.Information("ImageCare MCP server stopped");
    }
}

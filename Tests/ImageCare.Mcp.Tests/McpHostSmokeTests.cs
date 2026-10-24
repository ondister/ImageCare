using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;

using ImageCare.Core.Domain.Configuration;

using Microsoft.Extensions.Logging.Abstractions;

using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace ImageCare.Mcp.Tests;

/// <summary>
/// End-to-end checks that the embedded host really speaks MCP over Streamable HTTP and that the
/// REST surface and optional bearer token behave as documented. These tests use inert service
/// mocks: they validate transport, discovery and tool wiring, not media processing.
/// </summary>
[TestFixture]
public sealed class McpHostSmokeTests
{
    private static readonly string[] ExpectedTools =
    [
        "imagecare_folder_tags_get",
        "imagecare_folder_tags_propose",
        "imagecare_folder_tags_set",
        "imagecare_list_drives",
        "imagecare_list_folders",
        "imagecare_media_metadata",
        "imagecare_media_previews",
        "imagecare_open_folder",
        "imagecare_search",
        "imagecare_server_info",
        "imagecare_session_create",
        "imagecare_session_delete_remaining",
        "imagecare_session_get",
        "imagecare_session_next",
        "imagecare_session_stats",
        "imagecare_session_sync",
        "imagecare_tags_all",
        "imagecare_tags_apply",
        "imagecare_tags_suggest",
        "imagecare_ui_state"
    ];

    private string _directory = null!;
    private string _dataDirectory = null!;
    private ImageCareMcpHost? _host;

    [SetUp]
    public void SetUp()
    {
        _directory = TestSupport.CreateTempDirectory();
        _dataDirectory = Path.Combine(_directory, "mcp-data");
    }

    [TearDown]
    public async Task TearDown()
    {
        if (_host is not null)
        {
            await _host.DisposeAsync();
            _host = null;
        }

        TestSupport.DeleteTempDirectory(_directory);
    }

    [Test]
    public async Task Host_ListsToolsAndAnswersServerInfo()
    {
        _host = await StartHostAsync();
        Assert.That(_host.IsRunning, Is.True);
        Assert.That(_host.EndpointUrl, Does.EndWith("/mcp"));

        await using var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri(_host.EndpointUrl!),
                Name = "ImageCare.Mcp.Tests"
            },
            NullLoggerFactory.Instance);

        await using var client = await McpClient.CreateAsync(transport);

        var tools = await client.ListToolsAsync();
        var names = tools.Select(tool => tool.Name).ToArray();

        Assert.That(names, Is.SupersetOf(ExpectedTools));

        var serverInfo = await client.CallToolAsync("imagecare_server_info");
        var text = string.Concat(serverInfo.Content
            .OfType<TextContentBlock>()
            .Select(block => block.Text));

        Assert.That(text, Does.Contain("ImageCare.Mcp"));

        // Non-ASCII tags must stay readable in tool output instead of \uXXXX escapes.
        Assert.That(text, Does.Contain("птицы"));
        Assert.That(serverInfo.IsError, Is.Not.True);
    }

    [Test]
    public async Task Host_ServesHealthEndpoint()
    {
        _host = await StartHostAsync();
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        var response = await http.GetAsync(BaseUrl() + "/health");
        var body = await response.Content.ReadAsStringAsync();

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(body, Does.Contain("\"status\":\"ok\""));
        Assert.That(body, Does.Contain("streamable-http"));
    }

    [Test]
    public async Task Host_RejectsRequestsWithForeignHostHeader()
    {
        _host = await StartHostAsync();
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        using var request = new HttpRequestMessage(HttpMethod.Get, BaseUrl() + "/health");

        // DNS-rebinding attempt: a browser page resolving evil.example.com to 127.0.0.1.
        request.Headers.Host = "evil.example.com";

        var response = await http.SendAsync(request);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Host_RejectsRequestsWithoutBearerToken()
    {
        _host = await StartHostAsync("local-test-token");
        var endpoint = new Uri(_host.EndpointUrl!);
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        // Health stays open so that supervisors can probe the server without the token.
        var health = await http.GetAsync(BaseUrl() + "/health");
        Assert.That(health.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var unauthorized = await http.GetAsync(BaseUrl() + "/api/sessions");
        Assert.That(unauthorized.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));

        using var authorizedRequest = new HttpRequestMessage(HttpMethod.Get, BaseUrl() + "/api/sessions");
        authorizedRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "local-test-token");
        var authorized = await http.SendAsync(authorizedRequest);
        Assert.That(authorized.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        using var mcpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint);
        mcpRequest.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
        mcpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        mcpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        var mcpUnauthorized = await http.SendAsync(mcpRequest);
        Assert.That(mcpUnauthorized.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    private async Task<ImageCareMcpHost> StartHostAsync(string? accessToken = null)
    {
        var services = TestSupport.CreateServices(
            new Configuration
            {
                MediaSearchRootPaths = [_directory],
                PinnedTags = ["птицы"]
            },
            _dataDirectory);

        return await ImageCareMcpHost.StartAsync(
            services,
            new ImageCareMcpOptions
            {
                Port = 0,
                DataDirectory = _dataDirectory,
                AccessToken = accessToken
            });
    }

    private string BaseUrl()
    {
        const string suffix = "/mcp";
        var endpoint = _host!.EndpointUrl!;
        return endpoint.EndsWith(suffix, StringComparison.Ordinal)
            ? endpoint[..^suffix.Length]
            : endpoint;
    }
}

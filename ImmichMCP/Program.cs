using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ImmichMCP.Client;
using ImmichMCP.Configuration;
using ImmichMCP.Services;
using ImmichMCP.Tools.Gateway;
using Polly;
using Polly.Extensions.Http;

var useStdio = args.Contains("--stdio");

if (useStdio)
{
    // stdio transport for local usage (Claude Desktop)
    var builder = Host.CreateApplicationBuilder(args);

    ConfigureServices(builder.Services, builder.Configuration);

    builder.Logging.AddConsole(options =>
    {
        options.LogToStandardErrorThreshold = LogLevel.Trace;
    });

    builder.Services
        .AddMcpServer(options => ConfigureMcpCapabilities(options, builder.Configuration))
        .WithStdioServerTransport()
        .ConfigureTools(UseToolGateway(builder.Configuration));

    await builder.Build().RunAsync();
}
else
{
    // HTTP transport for remote usage
    var builder = WebApplication.CreateBuilder(args);

    ConfigureServices(builder.Services, builder.Configuration);

    builder.Services
        .AddMcpServer(options => ConfigureMcpCapabilities(options, builder.Configuration))
        .WithHttpTransport()
        .ConfigureTools(UseToolGateway(builder.Configuration));

    var app = builder.Build();

    var port = app.Configuration.GetValue<int?>("Mcp:Port")
               ?? (Environment.GetEnvironmentVariable("MCP_PORT") is string portStr && int.TryParse(portStr, out var p) ? p : 5000);

    app.MapMcp("/mcp");

    // Out-of-band upload endpoint
    app.MapPost("/upload/{sessionId}", async (
        string sessionId,
        HttpRequest request,
        UploadSessionService uploadService,
        ImmichClient immichClient,
        ILogger<Program> logger) =>
    {
        var session = uploadService.GetSession(sessionId);

        if (session == null)
        {
            return Results.NotFound(new { error = "Session not found", session_id = sessionId });
        }

        if (session.Status == UploadStatus.Expired)
        {
            return Results.BadRequest(new { error = "Session expired", session_id = sessionId });
        }

        if (session.Status == UploadStatus.Completed)
        {
            return Results.BadRequest(new { error = "Session already completed", asset_id = session.AssetId });
        }

        if (!request.HasFormContentType)
        {
            return Results.BadRequest(new { error = "Expected multipart/form-data" });
        }

        try
        {
            uploadService.UpdateSession(sessionId, s => s.Status = UploadStatus.Uploading);

            var form = await request.ReadFormAsync();
            var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();

            if (file == null || file.Length == 0)
            {
                uploadService.UpdateSession(sessionId, s =>
                {
                    s.Status = UploadStatus.Failed;
                    s.Error = "No file provided";
                });
                return Results.BadRequest(new { error = "No file provided in form data. Use field name 'file'." });
            }

            var fileName = session.FileName ?? file.FileName;
            logger.LogInformation("Receiving upload: {FileName} ({Size} bytes) for session {SessionId}",
                fileName, file.Length, sessionId);

            // Read file into memory
            using var memoryStream = new MemoryStream();
            await file.CopyToAsync(memoryStream);
            var fileBytes = memoryStream.ToArray();

            // Upload to Immich
            var asset = await immichClient.UploadAssetAsync(
                fileBytes,
                fileName,
                DateTime.UtcNow,
                session.IsFavorite,
                session.IsArchived
            );

            if (asset == null)
            {
                uploadService.UpdateSession(sessionId, s =>
                {
                    s.Status = UploadStatus.Failed;
                    s.Error = "Failed to upload to Immich";
                });
                return Results.Json(new { error = "Failed to upload asset to Immich" }, statusCode: 502);
            }

            uploadService.UpdateSession(sessionId, s =>
            {
                s.Status = UploadStatus.Completed;
                s.AssetId = asset.Id;
            });

            logger.LogInformation("Upload complete: {FileName} -> asset {AssetId}", fileName, asset.Id);

            return Results.Ok(new
            {
                success = true,
                asset_id = asset.Id,
                original_file_name = asset.OriginalFileName,
                type = asset.Type,
                session_id = sessionId
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Upload failed for session {SessionId}", sessionId);
            uploadService.UpdateSession(sessionId, s =>
            {
                s.Status = UploadStatus.Failed;
                s.Error = ex.Message;
            });
            return Results.Json(new { error = ex.Message, session_id = sessionId }, statusCode: 500);
        }
    }).DisableAntiforgery();

    // Health check endpoint (liveness only: is the process up, no upstream dependency)
    app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }));

    // Readiness endpoint: verifies the process can actually reach Immich.
    // Results are cached for a short window so frequent probe ticks don't hammer Immich.
    var readinessCache = new ReadinessCache();
    app.MapGet("/health/ready", async (ImmichClient immichClient, CancellationToken cancellationToken) =>
    {
        var (success, version, error) = await readinessCache.GetOrRefreshAsync(immichClient, cancellationToken).ConfigureAwait(false);

        return success
            ? Results.Ok(new { status = "ready", immich_connected = true, version })
            : Results.Json(new { status = "not_ready", immich_connected = false, error }, statusCode: StatusCodes.Status503ServiceUnavailable);
    });

    app.Logger.LogInformation("ImmichMCP server starting on port {Port}", port);
    app.Logger.LogInformation("MCP endpoint available at: http://localhost:{Port}/mcp", port);
    app.Logger.LogInformation("Upload endpoint available at: http://localhost:{Port}/upload/{{sessionId}}", port);

    await app.RunAsync($"http://0.0.0.0:{port}");
}

void ConfigureServices(IServiceCollection services, IConfiguration configuration)
{
    // Configuration
    services.Configure<ImmichOptions>(options =>
    {
        // Environment variables take precedence
        options.BaseUrl = Environment.GetEnvironmentVariable("IMMICH_BASE_URL")
                          ?? Environment.GetEnvironmentVariable("IMMICH_URL")
                          ?? configuration.GetValue<string>("Immich:BaseUrl")
                          ?? throw new InvalidOperationException("IMMICH_BASE_URL is required");

        options.ApiKey = Environment.GetEnvironmentVariable("IMMICH_API_KEY")
                         ?? Environment.GetEnvironmentVariable("IMMICH_TOKEN")
                         ?? configuration.GetValue<string>("Immich:ApiKey")
                         ?? throw new InvalidOperationException("IMMICH_API_KEY is required");

        options.MaxPageSize = Environment.GetEnvironmentVariable("MAX_PAGE_SIZE") is string maxPageStr && int.TryParse(maxPageStr, out var maxPage)
            ? maxPage
            : configuration.GetValue<int?>("Immich:MaxPageSize") ?? 100;

        options.DownloadMode = Environment.GetEnvironmentVariable("DOWNLOAD_MODE")
                               ?? configuration.GetValue<string>("Immich:DownloadMode")
                               ?? "url";

        options.MaxInlineDownloadBytes = Environment.GetEnvironmentVariable("MAX_INLINE_DOWNLOAD_BYTES") is string maxInlineStr && long.TryParse(maxInlineStr, out var maxInline)
            ? maxInline
            : configuration.GetValue<long?>("Immich:MaxInlineDownloadBytes") ?? 25 * 1024 * 1024;
    });

    // Configure retry policy for transient errors
    var retryPolicy = HttpPolicyExtensions
        .HandleTransientHttpError()
        .OrResult(msg => msg.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
        .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));

    // HttpClient for Immich API
    services.AddHttpClient<ImmichClient>((sp, client) =>
    {
        var options = sp.GetRequiredService<IOptions<ImmichOptions>>().Value;
        client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
        client.DefaultRequestHeaders.Add("Accept", "application/json");
        client.Timeout = TimeSpan.FromSeconds(120); // Longer timeout for uploads
    })
    .AddHttpMessageHandler<ImmichAuthHandler>()
    .AddPolicyHandler(retryPolicy);

    services.AddTransient<ImmichAuthHandler>();
    services.AddSingleton<UploadSessionService>();

    if (UseToolGateway(configuration))
    {
        services.AddImmichToolGateway();
    }
}

bool UseToolGateway(IConfiguration configuration)
{
    var mode = Environment.GetEnvironmentVariable("IMMICH_TOOL_MODE")
               ?? Environment.GetEnvironmentVariable("MCP_TOOL_MODE")
               ?? configuration.GetValue<string>("Mcp:ToolMode")
               ?? "static";

    return string.Equals(mode, "gateway", StringComparison.OrdinalIgnoreCase)
           || string.Equals(mode, "dynamic", StringComparison.OrdinalIgnoreCase);
}

void ConfigureMcpCapabilities(McpServerOptions options, IConfiguration configuration)
{
    if (!UseToolGateway(configuration))
    {
        return;
    }

    options.Capabilities ??= new ServerCapabilities();
    options.Capabilities.Tools ??= new ToolsCapability();
    options.Capabilities.Tools.ListChanged = true;
}

static class McpServerBuilderToolModeExtensions
{
    public static IMcpServerBuilder ConfigureTools(this IMcpServerBuilder builder, bool useToolGateway)
    {
        return useToolGateway
            ? builder.WithImmichToolGateway()
            : builder.WithToolsFromAssembly();
    }
}

/// <summary>
/// Caches the result of the last real Immich connectivity check so that frequent
/// readiness probes (e.g. Kubernetes hitting <c>/health/ready</c> every few seconds)
/// don't each translate into a live call to Immich. A new ping is only issued once
/// <see cref="CacheDurationSeconds"/> have elapsed since the previous one; callers that
/// arrive within the cache window get the last known result immediately, and callers
/// that arrive while a refresh is already running share that same refresh instead of
/// each starting their own.
/// </summary>
sealed class ReadinessCache
{
    private const int CacheDurationSeconds = 10;
    private const int PingTimeoutSeconds = 5;

    private readonly Lock _gate = new();
    private DateTime _lastCheckedUtc = DateTime.MinValue;
    private (bool Success, string? Version, string? Error) _lastResult = (false, null, "Not checked yet");
    private Task<(bool Success, string? Version, string? Error)>? _refreshInFlight;

    public Task<(bool Success, string? Version, string? Error)> GetOrRefreshAsync(ImmichClient client, CancellationToken cancellationToken)
    {
        Task<(bool Success, string? Version, string? Error)> refresh;
        lock (_gate)
        {
            if (DateTime.UtcNow - _lastCheckedUtc < TimeSpan.FromSeconds(CacheDurationSeconds))
            {
                return Task.FromResult(_lastResult);
            }

            refresh = _refreshInFlight ??= RefreshAsync(client);
        }

        // WaitAsync only stops this caller from waiting on cancellation; it never cancels the
        // shared refresh itself, so a caller aborting (e.g. a probe timing out) can't poison the
        // cache with a result other callers are still legitimately waiting on.
        return refresh.WaitAsync(cancellationToken);
    }

    private async Task<(bool Success, string? Version, string? Error)> RefreshAsync(ImmichClient client)
    {
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(PingTimeoutSeconds));

        (bool Success, string? Version, string? Error) result;
        try
        {
            var (success, info, error) = await client.PingAsync(timeoutCts.Token).ConfigureAwait(false);
            result = success
                ? (true, info?.Version, null)
                : (false, null, error ?? "Ping failed");
        }
        catch (OperationCanceledException)
        {
            result = (false, null, "Timed out waiting for Immich to respond");
        }

        lock (_gate)
        {
            _lastCheckedUtc = DateTime.UtcNow;
            _lastResult = result;
            _refreshInFlight = null;
        }

        return result;
    }
}

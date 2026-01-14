using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;
using ImmichMCP.Client;
using ImmichMCP.Configuration;
using ImmichMCP.Services;
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
        .AddMcpServer()
        .WithStdioServerTransport()
        .WithToolsFromAssembly();

    await builder.Build().RunAsync();
}
else
{
    // HTTP transport for remote usage
    var builder = WebApplication.CreateBuilder(args);

    ConfigureServices(builder.Services, builder.Configuration);

    // Register upload session service as singleton
    builder.Services.AddSingleton<UploadSessionService>();

    builder.Services
        .AddMcpServer()
        .WithHttpTransport(options =>
        {
            // Increase idle timeout to 24 hours to prevent session drops during long operations
            options.IdleTimeout = TimeSpan.FromHours(24);
        })
        .WithToolsFromAssembly();

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

            // Generate device asset ID
            var deviceAssetId = $"{fileName}-{fileBytes.Length}-{DateTime.UtcNow.Ticks}";

            // Upload to Immich
            var asset = await immichClient.UploadAssetAsync(
                fileBytes,
                fileName,
                deviceAssetId,
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

    // Health check endpoint
    app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }));

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
}

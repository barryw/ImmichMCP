using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;
using ImmichMCP.Client;
using ImmichMCP.Configuration;
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

    builder.Services
        .AddMcpServer()
        .WithHttpTransport()
        .WithToolsFromAssembly();

    var app = builder.Build();

    var port = app.Configuration.GetValue<int?>("Mcp:Port")
               ?? (Environment.GetEnvironmentVariable("MCP_PORT") is string portStr && int.TryParse(portStr, out var p) ? p : 5000);

    app.MapMcp("/mcp");

    app.Logger.LogInformation("ImmichMCP server starting on port {Port}", port);
    app.Logger.LogInformation("MCP endpoint available at: http://localhost:{Port}/mcp", port);

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

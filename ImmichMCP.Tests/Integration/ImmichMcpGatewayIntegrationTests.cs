using System.Text.Json;
using FluentAssertions;
using ImmichMCP.Tools.Gateway;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace ImmichMCP.Tests.Integration;

[Trait("Category", "McpIntegration")]
public class ImmichMcpGatewayIntegrationTests
{
    [McpIntegrationFact]
    public async Task GatewayModeEnablesHealthToolsAndPingsImmich()
    {
        await using var client = await CreateClient();

        var initialTools = await client.ListToolsAsync(new ListToolsRequestParams());
        initialTools.Tools.Select(tool => tool.Name).Should().BeEquivalentTo(
            ImmichToolGateway.ListToolsName,
            ImmichToolGateway.EnableToolsName);

        var enableResult = await client.CallToolAsync(
            ImmichToolGateway.EnableToolsName,
            new Dictionary<string, object?> { ["categories"] = new[] { "health" } });

        enableResult.IsError.Should().NotBeTrue(GetText(enableResult));

        var enabledTools = await client.ListToolsAsync(new ListToolsRequestParams());
        enabledTools.Tools.Select(tool => tool.Name).Should().Contain(["immich_ping", "immich_capabilities"]);

        var pingResult = await client.CallToolAsync("immich_ping", new Dictionary<string, object?>());
        pingResult.IsError.Should().NotBeTrue(GetText(pingResult));

        using var pingJson = JsonDocument.Parse(GetText(pingResult));
        pingJson.RootElement.GetProperty("ok").GetBoolean().Should().BeTrue();
        pingJson.RootElement.GetProperty("result").GetProperty("connected").GetBoolean().Should().BeTrue();
        var version = pingJson.RootElement.GetProperty("result").GetProperty("version").GetString();
        version.Should().NotBeNullOrWhiteSpace();
        version!.TrimStart('v').Should().StartWith("3.");
    }

    [McpIntegrationFact]
    public async Task GatewayModeAdvertisesAndEmitsToolListChanged()
    {
        var notification = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var client = await CreateClient(new McpClientHandlers
        {
            NotificationHandlers = new Dictionary<string, Func<JsonRpcNotification, CancellationToken, ValueTask>>
            {
                [NotificationMethods.ToolListChangedNotification] = (_, _) =>
                {
                    notification.TrySetResult();
                    return ValueTask.CompletedTask;
                }
            }
        });

        client.ServerCapabilities.Tools.Should().NotBeNull();
        client.ServerCapabilities.Tools!.ListChanged.Should().BeTrue();

        var enableResult = await client.CallToolAsync(
            ImmichToolGateway.EnableToolsName,
            new Dictionary<string, object?> { ["categories"] = new[] { "search" } });

        enableResult.IsError.Should().NotBeTrue(GetText(enableResult));
        await notification.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var enabledTools = await client.ListToolsAsync(new ListToolsRequestParams());
        enabledTools.Tools.Select(tool => tool.Name).Should().Contain("immich_search_metadata");
    }

    private static async Task<McpClient> CreateClient(McpClientHandlers? handlers = null)
    {
        var endpoint = Environment.GetEnvironmentVariable("IMMICH_MCP_BASE_URL") ?? "http://127.0.0.1:5000/mcp";
        return await McpClient.CreateAsync(
            new HttpClientTransport(
                new HttpClientTransportOptions
                {
                    Endpoint = new Uri(endpoint),
                    Name = "immichmcp-compose-smoke"
                },
                NullLoggerFactory.Instance),
            new McpClientOptions
            {
                ClientInfo = new Implementation
                {
                    Name = "immichmcp-gateway-integration-tests",
                    Version = "1.0.0"
                },
                Handlers = handlers ?? new McpClientHandlers()
            });
    }

    private static string GetText(CallToolResult result)
    {
        result.Content.Should().NotBeNullOrEmpty();
        var text = result.Content[0].Should().BeOfType<TextContentBlock>().Subject;
        return text.Text;
    }
}

public sealed class McpIntegrationFactAttribute : FactAttribute
{
    public McpIntegrationFactAttribute()
    {
        if (!IntegrationTestSettings.IsEnabled("IMMICH_MCP_INTEGRATION_TESTS"))
        {
            Skip = "Set IMMICH_MCP_INTEGRATION_TESTS=true to run MCP integration tests.";
        }
    }
}

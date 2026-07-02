using FluentAssertions;
using ImmichMCP.Client;
using ImmichMCP.Configuration;
using ImmichMCP.Services;
using ImmichMCP.Tools.Gateway;
using Microsoft.Extensions.DependencyInjection;

namespace ImmichMCP.Tests.Gateway;

public class ImmichToolGatewayTests
{
    [Fact]
    public void RegistryDiscoversAttributedImmichTools()
    {
        using var services = CreateServices();
        var registry = services.GetRequiredService<ImmichToolRegistry>();

        registry.Tools.Should().HaveCount(48);
        registry.Categories.Should().BeEquivalentTo(
            "activities",
            "albums",
            "assets",
            "health",
            "people",
            "search",
            "shared_links",
            "tags");
        registry.TryGetTool("immich_search_metadata", out var definition).Should().BeTrue();
        definition.Category.Should().Be("search");
        definition.Tool.ProtocolTool.InputSchema.GetProperty("type").GetString().Should().Be("object");
    }

    [Fact]
    public void GatewayStartsWithOnlyBootstrapTools()
    {
        using var services = CreateServices();
        var gateway = services.GetRequiredService<ImmichToolGateway>();

        var visibleToolNames = gateway.GetVisibleTools(new object()).Select(tool => tool.Name);

        visibleToolNames.Should().BeEquivalentTo(
            ImmichToolGateway.ListToolsName,
            ImmichToolGateway.EnableToolsName);
    }

    [Fact]
    public void EnabledCategoryAddsItsToolsToVisibleInventory()
    {
        using var services = CreateServices();
        var registry = services.GetRequiredService<ImmichToolRegistry>();
        var state = services.GetRequiredService<ImmichToolSessionState>();
        var gateway = services.GetRequiredService<ImmichToolGateway>();
        var session = new object();

        var searchTools = registry.ResolveToolNames([], ["search"]);
        state.Enable(session, searchTools);

        gateway.GetVisibleTools(session)
            .Select(tool => tool.Name)
            .Should()
            .BeEquivalentTo(
                ImmichToolGateway.ListToolsName,
                ImmichToolGateway.EnableToolsName,
                "immich_search_metadata",
                "immich_search_smart",
                "immich_search_ocr",
                "immich_search_explore");
    }

    [Fact]
    public void RegistryReportsUnknownToolAndCategorySelectors()
    {
        using var services = CreateServices();
        var registry = services.GetRequiredService<ImmichToolRegistry>();

        registry.GetUnknownSelectors(["immich_nope"], ["missing"]).Should().BeEquivalentTo(
            "category:missing",
            "tool:immich_nope");
    }

    private static ServiceProvider CreateServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        services.Configure<ImmichOptions>(options =>
        {
            options.BaseUrl = "http://immich.example.test";
            options.ApiKey = "test-key";
        });
        services.AddTransient<ImmichClient>(_ => throw new InvalidOperationException("Gateway unit tests do not invoke Immich."));
        services.AddSingleton<UploadSessionService>();
        services.AddImmichToolGateway();
        return services.BuildServiceProvider();
    }
}

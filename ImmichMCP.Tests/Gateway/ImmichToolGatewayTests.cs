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

        registry.Tools.Should().HaveCount(49);
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
    public void TagUpdateSchema_ExposesOnlyFieldsSupportedByImmich()
    {
        using var services = CreateServices();
        var registry = services.GetRequiredService<ImmichToolRegistry>();

        registry.TryGetTool("immich_tags_update", out var definition).Should().BeTrue();
        var properties = definition.Tool.ProtocolTool.InputSchema.GetProperty("properties");

        properties.TryGetProperty("color", out _).Should().BeTrue();
        properties.TryGetProperty("name", out _).Should().BeFalse();
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

    [Fact]
    public void EnablingEachCategory_ExposesExactlyThatCategorysToolsPlusBootstrap()
    {
        using var services = CreateServices();
        var registry = services.GetRequiredService<ImmichToolRegistry>();
        var state = services.GetRequiredService<ImmichToolSessionState>();
        var gateway = services.GetRequiredService<ImmichToolGateway>();

        foreach (var category in registry.Categories)
        {
            var session = new object();
            var expected = registry.ResolveToolNames([], [category]);
            expected.Should().NotBeEmpty($"category '{category}' should resolve to tools");

            state.Enable(session, expected);
            var visible = gateway.GetVisibleTools(session).Select(t => t.Name).ToList();

            visible.Should().Contain(ImmichToolGateway.ListToolsName);
            visible.Should().Contain(ImmichToolGateway.EnableToolsName);
            visible.Should().Contain(expected);
            visible.Should().HaveCount(expected.Count + 2,
                $"enabling '{category}' should expose only its tools plus the two bootstrap tools");
        }
    }

    [Fact]
    public void EnablingByExactToolName_ExposesThatTool()
    {
        using var services = CreateServices();
        var registry = services.GetRequiredService<ImmichToolRegistry>();
        var state = services.GetRequiredService<ImmichToolSessionState>();
        var gateway = services.GetRequiredService<ImmichToolGateway>();
        var session = new object();

        state.Enable(session, registry.ResolveToolNames(["immich_assets_upload_authorize"], []));

        gateway.GetVisibleTools(session).Select(t => t.Name)
            .Should().Contain("immich_assets_upload_authorize");
    }

    [Fact]
    public void ReadSelector_EnablesOnlyReadOnlyTools()
    {
        using var services = CreateServices();
        var registry = services.GetRequiredService<ImmichToolRegistry>();
        var state = services.GetRequiredService<ImmichToolSessionState>();
        var session = new object();

        var readTools = registry.ResolveToolNames([], ["read"]);

        readTools.Should().NotBeEmpty();
        readTools.Should().OnlyContain(name => registry.Tools.Single(t => t.Name == name).IsReadOnly);
        readTools.Should().NotContain("immich_assets_delete");
    }

    [Fact]
    public void AllSelector_EnablesEveryRegisteredTool()
    {
        using var services = CreateServices();
        var registry = services.GetRequiredService<ImmichToolRegistry>();

        registry.ResolveToolNames([], ["all"]).Should().HaveCount(registry.Tools.Count);
    }

    [Fact]
    public void EnabledTools_AreScopedPerSession()
    {
        using var services = CreateServices();
        var registry = services.GetRequiredService<ImmichToolRegistry>();
        var state = services.GetRequiredService<ImmichToolSessionState>();
        var gateway = services.GetRequiredService<ImmichToolGateway>();
        var sessionA = new object();
        var sessionB = new object();

        state.Enable(sessionA, registry.ResolveToolNames([], ["tags"]));

        gateway.GetVisibleTools(sessionA).Select(t => t.Name).Should().Contain("immich_tags_list");
        gateway.GetVisibleTools(sessionB).Select(t => t.Name).Should().BeEquivalentTo(
            ImmichToolGateway.ListToolsName, ImmichToolGateway.EnableToolsName);
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

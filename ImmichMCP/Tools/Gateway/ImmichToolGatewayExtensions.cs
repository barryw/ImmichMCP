using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace ImmichMCP.Tools.Gateway;

public static class ImmichToolGatewayExtensions
{
    public static IServiceCollection AddImmichToolGateway(this IServiceCollection services)
    {
        services.AddSingleton<ImmichToolSessionState>();
        services.AddSingleton<ImmichToolRegistry>();
        services.AddSingleton<ImmichToolGateway>();
        return services;
    }

    public static IMcpServerBuilder WithImmichToolGateway(this IMcpServerBuilder builder)
    {
        return builder
            .WithListToolsHandler((request, cancellationToken) =>
            {
                var services = request.Services ?? throw new InvalidOperationException("MCP request services are unavailable.");
                return services.GetRequiredService<ImmichToolGateway>().ListToolsAsync(request, cancellationToken);
            })
            .WithCallToolHandler((request, cancellationToken) =>
            {
                var services = request.Services ?? throw new InvalidOperationException("MCP request services are unavailable.");
                return services.GetRequiredService<ImmichToolGateway>().CallToolAsync(request, cancellationToken);
            });
    }
}

using System.ComponentModel;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace ImmichMCP.Tools.Gateway;

public sealed class ImmichToolRegistry
{
    private static readonly string[] ReadOnlyNameParts =
    [
        "_list",
        "_get",
        "_search",
        "_download",
        "_statistics",
        "_status",
        "_exif",
        "_explore"
    ];

    private readonly IReadOnlyDictionary<string, ImmichToolDefinition> _toolsByName;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<ImmichToolDefinition>> _toolsByCategory;

    public ImmichToolRegistry(IServiceProvider services)
    {
        var tools = DiscoverTools(services).OrderBy(tool => tool.Category, StringComparer.Ordinal)
            .ThenBy(tool => tool.Name, StringComparer.Ordinal)
            .ToArray();

        _toolsByName = tools.ToDictionary(tool => tool.Name, StringComparer.Ordinal);
        _toolsByCategory = tools.GroupBy(tool => tool.Category, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<ImmichToolDefinition>)group.ToArray(), StringComparer.Ordinal);
    }

    public IReadOnlyCollection<ImmichToolDefinition> Tools => _toolsByName.Values.ToArray();

    public IReadOnlyCollection<string> Categories => _toolsByCategory.Keys.Order(StringComparer.Ordinal).ToArray();

    public bool TryGetTool(string name, out ImmichToolDefinition definition)
    {
        return _toolsByName.TryGetValue(name, out definition!);
    }

    public IReadOnlyCollection<string> ResolveToolNames(IEnumerable<string> names, IEnumerable<string> categories)
    {
        var resolved = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var category in categories.Select(NormalizeCategory).Where(category => category.Length > 0))
        {
            if (string.Equals(category, "all", StringComparison.Ordinal))
            {
                foreach (var tool in _toolsByName.Values)
                {
                    resolved.Add(tool.Name);
                }

                continue;
            }

            if (string.Equals(category, "read", StringComparison.Ordinal) ||
                string.Equals(category, "readonly", StringComparison.Ordinal) ||
                string.Equals(category, "read_only", StringComparison.Ordinal))
            {
                foreach (var tool in _toolsByName.Values.Where(tool => tool.IsReadOnly))
                {
                    resolved.Add(tool.Name);
                }

                continue;
            }

            if (_toolsByCategory.TryGetValue(category, out var categoryTools))
            {
                foreach (var tool in categoryTools)
                {
                    resolved.Add(tool.Name);
                }
            }
        }

        foreach (var name in names.Select(name => name.Trim()).Where(name => name.Length > 0))
        {
            if (_toolsByName.ContainsKey(name))
            {
                resolved.Add(name);
            }
        }

        return resolved.ToArray();
    }

    public IReadOnlyCollection<string> GetUnknownSelectors(IEnumerable<string> names, IEnumerable<string> categories)
    {
        var unknown = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var category in categories.Select(NormalizeCategory).Where(category => category.Length > 0))
        {
            if (string.Equals(category, "all", StringComparison.Ordinal) ||
                string.Equals(category, "read", StringComparison.Ordinal) ||
                string.Equals(category, "readonly", StringComparison.Ordinal) ||
                string.Equals(category, "read_only", StringComparison.Ordinal) ||
                _toolsByCategory.ContainsKey(category))
            {
                continue;
            }

            unknown.Add($"category:{category}");
        }

        foreach (var name in names.Select(name => name.Trim()).Where(name => name.Length > 0))
        {
            if (!_toolsByName.ContainsKey(name))
            {
                unknown.Add($"tool:{name}");
            }
        }

        return unknown.ToArray();
    }

    private static IEnumerable<ImmichToolDefinition> DiscoverTools(IServiceProvider services)
    {
        var assembly = typeof(HealthTools).Assembly;

        foreach (var type in assembly.GetTypes().Where(type => type.GetCustomAttribute<McpServerToolTypeAttribute>() != null))
        {
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                         .Where(method => method.GetCustomAttribute<McpServerToolAttribute>() != null))
            {
                var attribute = method.GetCustomAttribute<McpServerToolAttribute>()!;
                var name = attribute.Name ?? method.Name;
                var description = method.GetCustomAttribute<DescriptionAttribute>()?.Description;
                var options = new McpServerToolCreateOptions
                {
                    Services = services,
                    Name = name,
                    Description = description,
                    Title = attribute.Title,
                    Destructive = attribute.Destructive ? true : null,
                    Idempotent = attribute.Idempotent ? true : null,
                    OpenWorld = attribute.OpenWorld ? true : null,
                    ReadOnly = attribute.ReadOnly ? true : null,
                    UseStructuredContent = attribute.UseStructuredContent
                };

                var tool = method.IsStatic
                    ? McpServerTool.Create(method, target: null, options)
                    : McpServerTool.Create(method, request =>
                    {
                        var requestServices = request.Services ?? services;
                        return ActivatorUtilities.CreateInstance(requestServices, type);
                    }, options);

                yield return new ImmichToolDefinition(
                    name,
                    GetCategory(name),
                    IsReadOnly(name, attribute),
                    tool);
            }
        }
    }

    private static bool IsReadOnly(string name, McpServerToolAttribute attribute)
    {
        if (attribute.ReadOnly)
        {
            return true;
        }

        if (name is "immich_ping" or "immich_capabilities")
        {
            return true;
        }

        return ReadOnlyNameParts.Any(part => name.Contains(part, StringComparison.Ordinal));
    }

    private static string GetCategory(string name)
    {
        if (name is "immich_ping" or "immich_capabilities")
        {
            return "health";
        }

        if (name.StartsWith("immich_shared_links_", StringComparison.Ordinal))
        {
            return "shared_links";
        }

        var remainder = name.StartsWith("immich_", StringComparison.Ordinal)
            ? name["immich_".Length..]
            : name;
        var separator = remainder.IndexOf('_', StringComparison.Ordinal);
        return separator > 0 ? remainder[..separator] : remainder;
    }

    private static string NormalizeCategory(string category)
    {
        return category.Trim().ToLowerInvariant().Replace('-', '_');
    }
}

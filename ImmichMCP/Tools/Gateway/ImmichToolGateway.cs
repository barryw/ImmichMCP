using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ImmichMCP.Client;
using ImmichMCP.Models.Common;

namespace ImmichMCP.Tools.Gateway;

public sealed class ImmichToolGateway
{
    public const string ListToolsName = "immich_tools_list";
    public const string EnableToolsName = "immich_tools_enable";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private static readonly Tool ListToolsTool = new()
    {
        Name = ListToolsName,
        Title = "List Immich tools",
        Description = "List available Immich MCP tool categories, currently enabled tools, and tools that can be enabled for this session.",
        InputSchema = CreateSchema("""
        {
          "type": "object",
          "properties": {
            "include_tools": {
              "type": "boolean",
              "description": "When true, include individual tools in each category. Defaults to true."
            }
          }
        }
        """),
        Annotations = new ToolAnnotations
        {
            ReadOnlyHint = true,
            DestructiveHint = false,
            IdempotentHint = true,
            OpenWorldHint = false
        }
    };

    private static readonly Tool EnableToolsTool = new()
    {
        Name = EnableToolsName,
        Title = "Enable Immich tools",
        Description = "Enable Immich MCP tools or categories for the current MCP session. Use immich_tools_list first to inspect available categories and tool names.",
        InputSchema = CreateSchema("""
        {
          "type": "object",
          "properties": {
            "categories": {
              "description": "Tool categories to enable. Supports health, search, assets, albums, people, tags, shared_links, activities, read, and all.",
              "oneOf": [
                { "type": "array", "items": { "type": "string" } },
                { "type": "string" }
              ]
            },
            "tools": {
              "description": "Exact tool names to enable.",
              "oneOf": [
                { "type": "array", "items": { "type": "string" } },
                { "type": "string" }
              ]
            }
          }
        }
        """),
        Annotations = new ToolAnnotations
        {
            ReadOnlyHint = false,
            DestructiveHint = false,
            IdempotentHint = true,
            OpenWorldHint = false
        }
    };

    private readonly ImmichToolRegistry _registry;
    private readonly ImmichToolSessionState _state;
    private readonly ILogger<ImmichToolGateway> _logger;

    public ImmichToolGateway(ImmichToolRegistry registry, ImmichToolSessionState state, ILogger<ImmichToolGateway> logger)
    {
        _registry = registry;
        _state = state;
        _logger = logger;
    }

    public ValueTask<ListToolsResult> ListToolsAsync(RequestContext<ListToolsRequestParams> request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _logger.LogDebug(
            "Listing Immich tools for gateway session {SessionKey}",
            ImmichToolSessionState.GetSessionKey(request));

        return ValueTask.FromResult(new ListToolsResult
        {
            Tools = GetVisibleTools(ImmichToolSessionState.GetSessionKey(request)).ToArray()
        });
    }

    public async ValueTask<CallToolResult> CallToolAsync(RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var toolName = request.Params?.Name;
        if (string.IsNullOrWhiteSpace(toolName))
        {
            return TextResult("Tool name is required.", isError: true);
        }

        var session = ImmichToolSessionState.GetSessionKey(request);
        _logger.LogDebug("Calling Immich gateway tool {ToolName} for session {SessionKey}", toolName, session);

        if (string.Equals(toolName, ListToolsName, StringComparison.Ordinal))
        {
            var includeTools = GetBoolean(request.Params?.Arguments, "include_tools") ?? true;
            return JsonResult(BuildInventory(session, includeTools));
        }

        if (string.Equals(toolName, EnableToolsName, StringComparison.Ordinal))
        {
            return await EnableToolsAsync(session, request, cancellationToken).ConfigureAwait(false);
        }

        if (!_state.IsEnabled(session, toolName))
        {
            return TextResult(
                $"Tool '{toolName}' is not enabled for this session. Call {EnableToolsName} with a matching category or tool name first.",
                isError: true);
        }

        if (!_registry.TryGetTool(toolName, out var definition))
        {
            return TextResult($"Tool '{toolName}' is not registered.", isError: true);
        }

        try
        {
            var result = await definition.Tool.InvokeAsync(request, cancellationToken).ConfigureAwait(false);
            // Tools serialize their own {"ok":false,...} error envelopes as text; per the MCP
            // spec a tool execution error must also flag the result with isError: true.
            return MarkErrorEnvelope(result);
        }
        catch (ImmichApiException ex)
        {
            _logger.LogError(ex, "Immich tool {ToolName} failed upstream ({StatusCode})", toolName, (int)ex.StatusCode);
            return JsonResult(BuildUpstreamError(ex), isError: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Dynamic Immich tool invocation failed for {ToolName}", toolName);
            return TextResult(ex.Message, isError: true);
        }
    }

    /// <summary>
    /// Flags a result as an error when the tool serialized an {"ok": false} envelope,
    /// so the transport-level isError matches the application-level outcome.
    /// </summary>
    internal static CallToolResult MarkErrorEnvelope(CallToolResult result)
    {
        if (result.IsError == true)
        {
            return result;
        }

        foreach (var block in result.Content)
        {
            if (block is TextContentBlock text && IsErrorEnvelope(text.Text))
            {
                return new CallToolResult { Content = result.Content, IsError = true };
            }
        }

        return result;
    }

    internal static bool IsErrorEnvelope(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text[0] != '{')
        {
            return false;
        }

        try
        {
            return JsonNode.Parse(text) is JsonObject obj
                && obj.TryGetPropertyValue("ok", out var ok)
                && ok is JsonValue value
                && value.TryGetValue<bool>(out var isOk)
                && !isOk;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    internal static object BuildUpstreamError(ImmichApiException ex)
    {
        var code = ex.StatusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => ErrorCodes.AuthFailed,
            HttpStatusCode.NotFound => ErrorCodes.NotFound,
            HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity => ErrorCodes.Validation,
            HttpStatusCode.TooManyRequests => ErrorCodes.RateLimit,
            _ => ErrorCodes.UpstreamError
        };

        return new
        {
            ok = false,
            error = new
            {
                code,
                message = ex.Message,
                details = new
                {
                    status = (int)ex.StatusCode,
                    path = ex.Path,
                    body = ex.ResponseBody
                }
            }
        };
    }

    public IReadOnlyCollection<Tool> GetVisibleTools(object? session)
    {
        var enabledTools = _state.GetEnabledTools(session);
        var tools = new List<Tool> { ListToolsTool, EnableToolsTool };

        foreach (var toolName in enabledTools)
        {
            if (_registry.TryGetTool(toolName, out var definition))
            {
                tools.Add(definition.Tool.ProtocolTool);
            }
        }

        return tools;
    }

    public object BuildInventory(object? session, bool includeTools = true)
    {
        var enabled = _state.GetEnabledTools(session).ToHashSet(StringComparer.Ordinal);
        var toolsByCategory = _registry.Tools
            .GroupBy(tool => tool.Category, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new
            {
                name = group.Key,
                enabled = group.Any(tool => enabled.Contains(tool.Name)),
                tool_count = group.Count(),
                enabled_tool_count = group.Count(tool => enabled.Contains(tool.Name)),
                tools = includeTools
                    ? group.OrderBy(tool => tool.Name, StringComparer.Ordinal)
                        .Select(tool => new
                        {
                            name = tool.Name,
                            description = tool.Tool.ProtocolTool.Description,
                            enabled = enabled.Contains(tool.Name),
                            read_only = tool.IsReadOnly
                        })
                        .ToArray()
                    : null
            })
            .ToArray();

        return new
        {
            mode = "gateway",
            bootstrap_tools = new[] { ListToolsName, EnableToolsName },
            enabled_tools = enabled.Order(StringComparer.Ordinal).ToArray(),
            categories = toolsByCategory,
            selectors = new
            {
                categories = _registry.Categories.Concat(["read", "all"]).Order(StringComparer.Ordinal).ToArray(),
                tools = _registry.Tools.Select(tool => tool.Name).Order(StringComparer.Ordinal).ToArray()
            },
            usage = new
            {
                enable_category = new { tool = EnableToolsName, arguments = new { categories = new[] { "search" } } },
                enable_tools = new { tool = EnableToolsName, arguments = new { tools = new[] { "immich_ping", "immich_capabilities" } } }
            }
        };
    }

    private async ValueTask<CallToolResult> EnableToolsAsync(
        object? session,
        RequestContext<CallToolRequestParams> request,
        CancellationToken cancellationToken)
    {
        var arguments = request.Params?.Arguments;
        var requestedCategories = GetStringList(arguments, "categories");
        var requestedTools = GetStringList(arguments, "tools");
        var resolvedTools = _registry.ResolveToolNames(requestedTools, requestedCategories);
        var unknown = _registry.GetUnknownSelectors(requestedTools, requestedCategories);

        if (resolvedTools.Count == 0)
        {
            var payload = new
            {
                enabled = false,
                error = "No matching tools or categories were requested.",
                unknown,
                valid_categories = _registry.Categories.Concat(["read", "all"]).Order(StringComparer.Ordinal).ToArray()
            };

            return JsonResult(payload, isError: true);
        }

        var enabledTools = _state.Enable(session, resolvedTools);
        await request.Server.SendNotificationAsync(NotificationMethods.ToolListChangedNotification, cancellationToken).ConfigureAwait(false);

        var payloadSuccess = new
        {
            enabled = true,
            enabled_now = resolvedTools,
            enabled_tools = enabledTools,
            unknown,
            visible_tool_count = GetVisibleTools(session).Count
        };

        return JsonResult(payloadSuccess);
    }

    private static CallToolResult JsonResult(object payload, bool isError = false)
    {
        return TextResult(JsonSerializer.Serialize(payload, JsonOptions), isError);
    }

    private static CallToolResult TextResult(string text, bool isError = false)
    {
        return new CallToolResult
        {
            Content =
            [
                new TextContentBlock
                {
                    Text = text
                }
            ],
            IsError = isError
        };
    }

    private static bool? GetBoolean(IDictionary<string, JsonElement>? arguments, string name)
    {
        if (arguments == null || !arguments.TryGetValue(name, out var element))
        {
            return null;
        }

        return element.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(element.GetString(), out var value) => value,
            _ => null
        };
    }

    private static IReadOnlyCollection<string> GetStringList(IDictionary<string, JsonElement>? arguments, string name)
    {
        if (arguments == null || !arguments.TryGetValue(name, out var element))
        {
            return [];
        }

        return element.ValueKind switch
        {
            JsonValueKind.String => SplitSelectorString(element.GetString()),
            JsonValueKind.Array => element.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .SelectMany(item => SplitSelectorString(item.GetString()))
                .ToArray(),
            _ => []
        };
    }

    private static IReadOnlyCollection<string> SplitSelectorString(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    }

    private static JsonElement CreateSchema(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}

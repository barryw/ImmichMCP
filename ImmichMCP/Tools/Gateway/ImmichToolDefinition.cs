using ModelContextProtocol.Server;

namespace ImmichMCP.Tools.Gateway;

public sealed record ImmichToolDefinition(
    string Name,
    string Category,
    bool IsReadOnly,
    McpServerTool Tool);

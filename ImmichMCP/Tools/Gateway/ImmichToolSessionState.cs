using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using ModelContextProtocol.Server;

namespace ImmichMCP.Tools.Gateway;

public sealed class ImmichToolSessionState
{
    private static readonly object DefaultSession = new();

    private readonly ConcurrentDictionary<object, ConcurrentDictionary<string, byte>> _enabledTools =
        new(SessionKeyComparer.Instance);

    public IReadOnlyCollection<string> GetEnabledTools(object? session)
    {
        var tools = GetSessionTools(session);
        return tools.Keys.Order(StringComparer.Ordinal).ToArray();
    }

    public bool IsEnabled(object? session, string toolName)
    {
        return GetSessionTools(session).ContainsKey(toolName);
    }

    public IReadOnlyCollection<string> Enable(object? session, IEnumerable<string> toolNames)
    {
        var tools = GetSessionTools(session);

        foreach (var toolName in toolNames)
        {
            tools.TryAdd(toolName, 0);
        }

        return tools.Keys.Order(StringComparer.Ordinal).ToArray();
    }

    public static object GetSessionKey<TParams>(RequestContext<TParams>? request)
    {
        var serverSessionId = request?.Server?.SessionId;
        if (!string.IsNullOrWhiteSpace(serverSessionId))
        {
            return $"server:{serverSessionId}";
        }

        var transportSessionId = request?.JsonRpcRequest?.Context?.RelatedTransport?.SessionId;
        if (!string.IsNullOrWhiteSpace(transportSessionId))
        {
            return $"transport:{transportSessionId}";
        }

        return request?.Server ?? DefaultSession;
    }

    private ConcurrentDictionary<string, byte> GetSessionTools(object? session)
    {
        return _enabledTools.GetOrAdd(session ?? DefaultSession, _ => new ConcurrentDictionary<string, byte>(StringComparer.Ordinal));
    }

    private sealed class SessionKeyComparer : IEqualityComparer<object>
    {
        public static readonly SessionKeyComparer Instance = new();

        public new bool Equals(object? x, object? y)
        {
            if (x is string xString && y is string yString)
            {
                return string.Equals(xString, yString, StringComparison.Ordinal);
            }

            return ReferenceEquals(x, y);
        }

        public int GetHashCode(object obj)
        {
            if (obj is string value)
            {
                return StringComparer.Ordinal.GetHashCode(value);
            }

            return RuntimeHelpers.GetHashCode(obj);
        }
    }
}

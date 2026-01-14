using System.Collections.Concurrent;

namespace ImmichMCP.Services;

/// <summary>
/// Represents a pending upload session.
/// </summary>
public record UploadSession
{
    public string SessionId { get; init; } = Guid.NewGuid().ToString();
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; init; }
    public string? FileName { get; init; }
    public bool? IsFavorite { get; init; }
    public bool? IsArchived { get; init; }
    public UploadStatus Status { get; set; } = UploadStatus.Pending;
    public string? AssetId { get; set; }
    public string? Error { get; set; }
}

public enum UploadStatus
{
    Pending,
    Uploading,
    Completed,
    Failed,
    Expired
}

/// <summary>
/// Manages upload sessions for out-of-band file uploads.
/// </summary>
public class UploadSessionService : IDisposable
{
    private readonly ConcurrentDictionary<string, UploadSession> _sessions = new();
    private readonly TimeSpan _sessionTimeout;
    private readonly Timer _cleanupTimer;

    public UploadSessionService(TimeSpan? sessionTimeout = null)
    {
        _sessionTimeout = sessionTimeout ?? TimeSpan.FromMinutes(30);
        _cleanupTimer = new Timer(CleanupExpiredSessions, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    /// <summary>
    /// Creates a new upload session.
    /// </summary>
    public UploadSession CreateSession(string? fileName = null, bool? isFavorite = null, bool? isArchived = null)
    {
        var session = new UploadSession
        {
            ExpiresAt = DateTime.UtcNow.Add(_sessionTimeout),
            FileName = fileName,
            IsFavorite = isFavorite,
            IsArchived = isArchived
        };

        _sessions[session.SessionId] = session;
        return session;
    }

    /// <summary>
    /// Gets a session by ID.
    /// </summary>
    public UploadSession? GetSession(string sessionId)
    {
        if (_sessions.TryGetValue(sessionId, out var session))
        {
            if (session.ExpiresAt < DateTime.UtcNow)
            {
                session.Status = UploadStatus.Expired;
            }
            return session;
        }
        return null;
    }

    /// <summary>
    /// Updates a session's status.
    /// </summary>
    public bool UpdateSession(string sessionId, Action<UploadSession> update)
    {
        if (_sessions.TryGetValue(sessionId, out var session))
        {
            update(session);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Removes a session.
    /// </summary>
    public bool RemoveSession(string sessionId)
    {
        return _sessions.TryRemove(sessionId, out _);
    }

    private void CleanupExpiredSessions(object? state)
    {
        var expiredIds = _sessions
            .Where(kvp => kvp.Value.ExpiresAt < DateTime.UtcNow)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var id in expiredIds)
        {
            _sessions.TryRemove(id, out _);
        }
    }

    public void Dispose()
    {
        _cleanupTimer.Dispose();
    }
}

using IFB.Shared.Enums;
using System.Collections.Concurrent;

namespace IFB.Server.Services;

/// <summary>
/// Tracks user online presence and SignalR connection IDs.
/// In-process implementation — swap for Redis-backed version for multi-server deployments.
/// </summary>
public interface IPresenceTracker
{
    void UserConnected(string userId, string connectionId);
    void UserDisconnected(string userId, string connectionId);
    bool IsOnline(string userId);
    void SetStatus(string userId, PresenceStatus status);
    PresenceStatus GetStatus(string userId);
    IReadOnlyList<string> GetOnlineUserIds();
    IReadOnlyList<string> GetConnectionIds(string userId);
}

public sealed class InMemoryPresenceTracker : IPresenceTracker
{
    public IReadOnlyList<string> GetConnectionIds(string userId)
    { lock (_lock) return _connections.TryGetValue(userId, out var connections) ? connections.ToArray() : []; }
    // userId → set of active connectionIds (multiple tabs/devices per user)
    private readonly ConcurrentDictionary<string, HashSet<string>> _connections = new();
    private readonly ConcurrentDictionary<string, PresenceStatus> _statuses = new();
    private readonly object _lock = new();

    public void UserConnected(string userId, string connectionId)
    {
        lock (_lock)
        {
            if (!_connections.TryGetValue(userId, out var set))
            {
                set = [];
                _connections[userId] = set;
            }
            set.Add(connectionId);
        }
        _statuses[userId] = PresenceStatus.Online;
    }

    public void UserDisconnected(string userId, string connectionId)
    {
        lock (_lock)
        {
            if (!_connections.TryGetValue(userId, out var set)) return;
            set.Remove(connectionId);
            if (set.Count == 0)
            {
                _connections.TryRemove(userId, out _);
                _statuses[userId] = PresenceStatus.Offline;
            }
        }
    }

    public bool IsOnline(string userId)
    { lock (_lock) return _connections.TryGetValue(userId, out var set) && set.Count > 0; }

    public void SetStatus(string userId, PresenceStatus status) =>
        _statuses[userId] = status;

    public PresenceStatus GetStatus(string userId) =>
        _statuses.GetValueOrDefault(userId, PresenceStatus.Offline);

    public IReadOnlyList<string> GetOnlineUserIds() =>
        [.. _connections.Keys];
}

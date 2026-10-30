using System.Collections.Concurrent;

namespace DrinkWater.Api.Services;

/// <summary>SignalR offers no "is this user connected" API, so the hub keeps the count.
/// It is per-process state; a second API instance would need a shared store.</summary>
public sealed class PresenceTracker
{
    private readonly ConcurrentDictionary<Guid, int> _connections = new();

    public void Add(Guid userId) =>
        _connections.AddOrUpdate(userId, 1, (_, count) => count + 1);

    public void Remove(Guid userId) =>
        _connections.AddOrUpdate(userId, 0, (_, count) => Math.Max(0, count - 1));

    public bool IsOnline(Guid userId) =>
        _connections.TryGetValue(userId, out var count) && count > 0;
}


using DrinkWater.Api.Services;
using Microsoft.AspNetCore.SignalR;

namespace DrinkWater.Api.Hubs;

public class NotificationHub(PresenceTracker presence) : Hub
{
    public override Task OnConnectedAsync()
    {
        presence.Add(UserId());
        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        presence.Remove(UserId());
        return base.OnDisconnectedAsync(exception);
    }

    private Guid UserId() =>
        Guid.Parse(Context.UserIdentifier ?? throw new InvalidOperationException("Hub cần user đã đăng nhập"));
}

using IFB.Server.Data;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Threading.RateLimiting;

namespace IFB.Server.Hubs;

public sealed class ChatPolicyFilter : IHubFilter, IDisposable
{
    private readonly PartitionedRateLimiter<string> _limiter;
    private readonly SemaphoreSlim _sqliteWrites = new(1, 1);
    private readonly bool _sqlite;
    public ChatPolicyFilter(IConfiguration config)
    {
        _sqlite = config["Database:Provider"] != "SqlServer";
        var limit = config.GetValue("RateLimiting:MessagesPerMinutePerUser", 60);
        _limiter = PartitionedRateLimiter.Create<string, string>(id =>
            RateLimitPartition.GetFixedWindowLimiter(id, _ => new FixedWindowRateLimiterOptions
            { PermitLimit = limit, Window = TimeSpan.FromMinutes(1), AutoReplenishment = true }));
    }
    public async ValueTask<object?> InvokeMethodAsync(HubInvocationContext context,
        Func<HubInvocationContext, ValueTask<object?>> next)
    {
        var id = context.Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        var db = context.ServiceProvider.GetRequiredService<AppDbContext>();
        var version = context.Context.User?.FindFirstValue("token_version");
        if (!await db.Users.AnyAsync(u => u.Id == id && u.IsActive && u.TokenVersion.ToString() == version))
        { context.Context.Abort(); throw new HubException("Account unavailable. Please sign in again."); }
        if (context.HubMethodName == nameof(ChatHub.SendMessageAsync))
        {
            using var lease = _limiter.AttemptAcquire(id!);
            if (!lease.IsAcquired) throw new HubException("Message limit reached. Please wait a minute.");
        }
        var mutates = context.HubMethodName != nameof(ChatHub.SendTypingAsync) && context.HubMethodName != nameof(ChatHub.JoinConversationAsync) && context.HubMethodName != nameof(ChatHub.LeaveConversationAsync);
        if (_sqlite && mutates) await _sqliteWrites.WaitAsync(context.Context.ConnectionAborted);
        try { return await next(context); }
        finally { if (_sqlite && mutates) _sqliteWrites.Release(); }
    }
    public async Task OnConnectedAsync(HubLifetimeContext context, Func<HubLifetimeContext, Task> next)
    {
        if (_sqlite) await _sqliteWrites.WaitAsync(context.Context.ConnectionAborted);
        try { await next(context); } finally { if (_sqlite) _sqliteWrites.Release(); }
    }
    public async Task OnDisconnectedAsync(HubLifetimeContext context, Exception? exception, Func<HubLifetimeContext, Exception?, Task> next)
    {
        if (_sqlite) await _sqliteWrites.WaitAsync();
        try { await next(context, exception); } finally { if (_sqlite) _sqliteWrites.Release(); }
    }
    public void Dispose() { _limiter.Dispose(); _sqliteWrites.Dispose(); }
}

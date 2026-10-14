using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace IFB.Server.Workers;

/// <summary>
/// Listens on UDP port 5500 for subnet broadcast discovery requests.
/// Replies with the server's address so clients can auto-discover it.
/// </summary>
public sealed class UdpDiscoveryWorker : BackgroundService
{
    private readonly IConfiguration _config;
    private readonly ILogger<UdpDiscoveryWorker> _logger;
    private const string DiscoveryRequest = "IFB_DISCOVER_v1";

    public UdpDiscoveryWorker(IConfiguration config, ILogger<UdpDiscoveryWorker> logger)
    {
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!bool.Parse(_config["Discovery:Enabled"] ?? "true"))
        {
            _logger.LogInformation("UDP discovery disabled by config.");
            return;
        }

        var port = int.Parse(_config["Discovery:UdpPort"] ?? "5500");
        var serverHost = _config["Discovery:ServerHost"] ?? "localhost";
        var serverPort = _config["Discovery:ServerPort"] ?? "5001";

        var bindAddress = IPAddress.Parse(_config["Server:BindAddress"] ?? "127.0.0.1");
        using var udp = new UdpClient(new IPEndPoint(bindAddress, port));
        udp.EnableBroadcast = true;

        _logger.LogInformation("UDP discovery listening on port {Port}", port);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var result = await udp.ReceiveAsync(ct);
                var msg = Encoding.UTF8.GetString(result.Buffer);

                if (msg.Trim() == DiscoveryRequest)
                {
                    _logger.LogDebug("Discovery request from {EP}", result.RemoteEndPoint);
                    var reply = JsonSerializer.SerializeToUtf8Bytes(new
                    {
                        host = serverHost,
                        port = int.Parse(serverPort),
                        version = "1.0",
                        product = "IFBConnect"
                    });
                    await udp.SendAsync(reply, result.RemoteEndPoint, ct);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UDP discovery error");
                try { await Task.Delay(1000, ct); } catch (OperationCanceledException) { break; }
            }
        }
    }
}

/// <summary>
/// Runs nightly to delete messages older than the conversation's retention period.
/// </summary>
public sealed class RetentionWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<RetentionWorker> _logger;

    public RetentionWorker(IServiceScopeFactory scopeFactory, IConfiguration config, ILogger<RetentionWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var intervalHours = Math.Max(1, int.Parse(_config["Retention:RunCleanupIntervalHours"] ?? "24"));

        // Run shortly after startup (not a full interval later) so servers restarted daily still clean up.
        try { await Task.Delay(TimeSpan.FromMinutes(2), ct); } catch (OperationCanceledException) { return; }
        while (!ct.IsCancellationRequested)
        {
            try { await RunCleanupAsync(ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception ex) { _logger.LogError(ex, "Retention cleanup failed; it will be retried at the next interval."); }
            try { await Task.Delay(TimeSpan.FromHours(intervalHours), ct); } catch (OperationCanceledException) { return; }
        }
    }

    private async Task RunCleanupAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IFB.Server.Data.AppDbContext>();

        var conversations = await db.Conversations.ToListAsync(ct);
        int deleted = 0;

        foreach (var conv in conversations)
        {
            var cutoff = DateTime.UtcNow.AddDays(-conv.RetentionDays);
            var old = db.Messages.Where(m => m.ConversationId == conv.Id && m.SentAt < cutoff);
            await db.Messages.Where(m => m.ReplyToId != null && db.Messages.Any(oldMessage => oldMessage.Id == m.ReplyToId && oldMessage.ConversationId == conv.Id && oldMessage.SentAt < cutoff))
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.ReplyToId, (string?)null), ct);
            await db.OfflineMessages.Where(o => db.Messages.Any(m => m.Id == o.MessageId && m.ConversationId == conv.Id && m.SentAt < cutoff)).ExecuteDeleteAsync(ct);
            var oldFiles = await db.FileAttachments.Where(f => f.ConversationId == conv.Id && f.UploadedAt < cutoff).ToListAsync(ct);
            foreach (var file in oldFiles)
            {
                try { if (System.IO.File.Exists(file.StoragePath)) System.IO.File.Delete(file.StoragePath); }
                catch (IOException ex) { _logger.LogWarning(ex, "Could not delete expired attachment {FileId}", file.Id); }
                catch (UnauthorizedAccessException ex) { _logger.LogWarning(ex, "Could not delete expired attachment {FileId}", file.Id); }
            }
            db.FileAttachments.RemoveRange(oldFiles); await db.SaveChangesAsync(ct);
            deleted += await old.ExecuteDeleteAsync(ct);
        }

        var auditDays = _config.GetValue("Retention:AuditDays", 365);
        var auditDeleted = 0;
        if (auditDays > 0)
        {
            var auditCutoff = DateTime.UtcNow.AddDays(-auditDays);
            auditDeleted = await db.AuditLogs.Where(l => l.Timestamp < auditCutoff).ExecuteDeleteAsync(ct);
        }

        _logger.LogInformation("Retention cleanup: deleted {Count} messages and {AuditCount} audit entries", deleted, auditDeleted);
    }
}

/// <summary>
/// Periodically attempts to deliver queued offline messages to reconnected users.
/// (The main delivery path is OnConnectedAsync in ChatHub; this is a safety net.)
/// </summary>
public sealed class OfflineQueueWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OfflineQueueWorker> _logger;

    public OfflineQueueWorker(IServiceScopeFactory scopeFactory, ILogger<OfflineQueueWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // Clean up delivered messages every 10 minutes
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(TimeSpan.FromMinutes(10), ct); } catch (OperationCanceledException) { return; }
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<IFB.Server.Data.AppDbContext>();
                var cutoff = DateTime.UtcNow.AddDays(-7);
                await db.OfflineMessages
                    .Where(o => o.IsDelivered || o.QueuedAt < cutoff)
                    .ExecuteDeleteAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception ex) { _logger.LogError(ex, "Offline queue cleanup failed; it will be retried."); }
        }
    }
}



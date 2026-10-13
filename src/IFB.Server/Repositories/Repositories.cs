using IFB.Server.Data;
using IFB.Server.Models;
using IFB.Shared.DTOs;
using IFB.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace IFB.Server.Repositories;

// ── Message Repository ────────────────────────────────────────────────────────

public interface IMessageRepository
{
    Task<Message> CreateAsync(string conversationId, string senderId, string content,
        MessageType type, string? replyToId, string? fileId, CancellationToken ct = default);
    Task<Message?> GetByIdAsync(string id);
    Task<List<Message>> GetHistoryAsync(string conversationId, DateTime before, int limit);
    Task UpdateAsync(Message message);
    Task<int> GetUnreadCountAsync(string conversationId, string userId);
}

public sealed class MessageRepository : IMessageRepository
{
    private readonly AppDbContext _db;
    public MessageRepository(AppDbContext db) => _db = db;

    public async Task<Message> CreateAsync(
        string conversationId, string senderId, string content,
        MessageType type, string? replyToId, string? fileId,
        CancellationToken ct = default)
    {
        var msg = new Message
        {
            Id = Guid.NewGuid().ToString(),
            ConversationId = conversationId,
            SenderId = senderId,
            Content = content,
            Type = type,
            ReplyToId = replyToId,
            FileId = fileId,
            SentAt = DateTime.UtcNow
        };
        _db.Messages.Add(msg);

        // Update conversation LastActivity
        var conv = await _db.Conversations.FindAsync([conversationId], ct);
        if (conv != null) conv.LastActivity = msg.SentAt;

        await _db.SaveChangesAsync(ct);
        return msg;
    }

    public Task<Message?> GetByIdAsync(string id) =>
        _db.Messages
            .Include(m => m.Sender)
            .Include(m => m.Reactions)
            .FirstOrDefaultAsync(m => m.Id == id);

    public Task<List<Message>> GetHistoryAsync(string conversationId, DateTime before, int limit) =>
        _db.Messages
            .Where(m => m.ConversationId == conversationId && m.SentAt < before)
            .OrderByDescending(m => m.SentAt)
            .Take(limit)
            .Include(m => m.Sender)
            .Include(m => m.Reactions)
            .AsNoTracking()
            .ToListAsync();

    public async Task UpdateAsync(Message message)
    {
        _db.Messages.Update(message);
        await _db.SaveChangesAsync();
    }

    public async Task<int> GetUnreadCountAsync(string conversationId, string userId)
    {
        // Get the timestamp of the last read message for this user
        var lastRead = await _db.MessageReadReceipts
            .Where(r => r.UserId == userId)
            .Join(_db.Messages.Where(m => m.ConversationId == conversationId),
                r => r.MessageId, m => m.Id, (r, m) => m.SentAt)
            .MaxAsync(dt => (DateTime?)dt);

        var query = _db.Messages
            .Where(m => m.ConversationId == conversationId
                     && !m.IsDeleted
                     && m.SenderId != userId);

        if (lastRead.HasValue)
            query = query.Where(m => m.SentAt > lastRead.Value);

        return await query.CountAsync();
    }
}

// ── Conversation Repository ───────────────────────────────────────────────────

public interface IConversationRepository
{
    Task<List<Conversation>> GetForUserAsync(string userId);
    Task<Conversation?> GetByIdAsync(string id);
    Task<bool> IsMemberAsync(string conversationId, string userId);
}

public sealed class ConversationRepository : IConversationRepository
{
    private readonly AppDbContext _db;
    public ConversationRepository(AppDbContext db) => _db = db;

    public Task<List<Conversation>> GetForUserAsync(string userId) =>
        _db.ConversationMembers
            .Where(m => m.UserId == userId)
            .Include(m => m.Conversation)
                .ThenInclude(c => c.Members)
                    .ThenInclude(m => m.User)
            .Select(m => m.Conversation)
            .Where(c => !c.IsArchived)
            .OrderByDescending(c => c.LastActivity)
            .AsNoTracking()
            .ToListAsync();

    public Task<Conversation?> GetByIdAsync(string id) =>
        _db.Conversations
            .Include(c => c.Members).ThenInclude(m => m.User)
            .Include(c => c.Messages.OrderByDescending(m => m.SentAt).Take(1))
            .FirstOrDefaultAsync(c => c.Id == id);

    public Task<bool> IsMemberAsync(string conversationId, string userId) =>
        _db.ConversationMembers
            .AnyAsync(m => m.ConversationId == conversationId && m.UserId == userId);
}

// ── Audit Repository ──────────────────────────────────────────────────────────

public interface IAuditRepository
{
    Task LogAsync(string userId, string action, string details, string? ipAddress = null);
    Task<List<AuditLog>> GetUserLogsAsync(string userId, int limit = 100);
    Task<List<AuditLog>> GetRecentAsync(int limit = 500);
}

public sealed class AuditRepository : IAuditRepository
{
    private readonly AppDbContext _db;
    public AuditRepository(AppDbContext db) => _db = db;

    public async Task LogAsync(string userId, string action, string details, string? ipAddress = null)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid().ToString(),
            UserId = userId,
            Action = action,
            Details = details,
            IpAddress = ipAddress,
            Timestamp = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
    }

    public Task<List<AuditLog>> GetUserLogsAsync(string userId, int limit = 100) =>
        _db.AuditLogs
            .Where(l => l.UserId == userId)
            .OrderByDescending(l => l.Timestamp)
            .Take(limit)
            .AsNoTracking()
            .ToListAsync();

    public Task<List<AuditLog>> GetRecentAsync(int limit = 500) =>
        _db.AuditLogs
            .OrderByDescending(l => l.Timestamp)
            .Take(limit)
            .AsNoTracking()
            .ToListAsync();
}

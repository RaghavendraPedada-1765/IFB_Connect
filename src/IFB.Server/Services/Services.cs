using IFB.Server.Data;
using IFB.Server.Models;
using IFB.Shared.DTOs;
using IFB.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace IFB.Server.Services;

// ── Message Service ───────────────────────────────────────────────────────────

public interface IMessageService
{
    Task<(Message msg, MessageDto dto)> SaveMessageAsync(
        string conversationId, string senderId, string content,
        MessageType type, string? replyToId, string? fileId,
        CancellationToken ct = default);
}

public sealed class MessageService : IMessageService
{
    private readonly AppDbContext _db;
    public MessageService(AppDbContext db) => _db = db;

    public async Task<(Message msg, MessageDto dto)> SaveMessageAsync(
        string conversationId, string senderId, string content,
        MessageType type, string? replyToId, string? fileId,
        CancellationToken ct = default)
    {
        var sanitized = SanitizeContent(content);
        if (sanitized.Length > 10_000 || !Enum.IsDefined(type))
            throw new Microsoft.AspNetCore.SignalR.HubException("Invalid message or message exceeds 10,000 characters.");
        if (replyToId != null && !await _db.Messages.AnyAsync(m =>
            m.Id == replyToId && m.ConversationId == conversationId && !m.IsDeleted, ct))
            throw new Microsoft.AspNetCore.SignalR.HubException("Reply target is unavailable.");
        if (fileId != null && !await _db.FileAttachments.AnyAsync(f =>
            f.Id == fileId && f.ConversationId == conversationId, ct))
            throw new Microsoft.AspNetCore.SignalR.HubException("Attachment does not belong to this conversation.");

        var msg = new Message
        {
            Id = Guid.NewGuid().ToString(),
            ConversationId = conversationId,
            SenderId = senderId,
            Content = sanitized,
            Type = type,
            ReplyToId = replyToId,
            FileId = fileId,
            SentAt = DateTime.UtcNow
        };

        _db.Messages.Add(msg);

        var conv = await _db.Conversations.FindAsync([conversationId], ct);
        if (conv != null) conv.LastActivity = msg.SentAt;

        await _db.SaveChangesAsync(ct);

        // Load sender for the DTO
        var sender = await _db.Users.FindAsync([senderId], ct);

        var dto = new MessageDto(
            msg.Id, msg.ConversationId, msg.SenderId,
            sender?.DisplayName ?? "Unknown",
            sender?.AvatarPath,
            msg.Content, msg.Type,
            msg.ReplyToId, msg.FileId,
            msg.IsEdited, msg.SentAt, []);

        return (msg, dto);
    }

    public static string SanitizeContent(string content)
    {
        if (string.IsNullOrEmpty(content)) return content;
        // Strip null bytes and basic XSS patterns for text storage
        return content
            .Replace("\0", "")
            .Trim();
    }
}

// ── Conversation Service ──────────────────────────────────────────────────────

public interface IConversationService
{
    Task<bool> IsMemberAsync(string conversationId, string userId);
    Task UpdateLastActivityAsync(string conversationId, DateTime timestamp);
}

public sealed class ConversationService : IConversationService
{
    private readonly AppDbContext _db;
    public ConversationService(AppDbContext db) => _db = db;

    public Task<bool> IsMemberAsync(string conversationId, string userId) =>
        _db.ConversationMembers
            .AnyAsync(m => m.ConversationId == conversationId && m.UserId == userId && !m.Conversation.IsArchived);

    public async Task UpdateLastActivityAsync(string conversationId, DateTime timestamp)
    {
        var conv = await _db.Conversations.FindAsync(conversationId);
        if (conv != null)
        {
            conv.LastActivity = timestamp;
            await _db.SaveChangesAsync();
        }
    }
}

// ── Audit Service ─────────────────────────────────────────────────────────────

public interface IAuditService
{
    Task LogAsync(string userId, string action, string details, string? ip = null);
}

public sealed class AuditService : IAuditService
{
    private readonly AppDbContext _db;
    private readonly ILogger<AuditService> _logger;

    public AuditService(AppDbContext db, ILogger<AuditService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task LogAsync(string userId, string action, string details, string? ip = null)
    {
        try
        {
            _db.AuditLogs.Add(new AuditLog
            {
                Id = Guid.NewGuid().ToString(),
                UserId = userId,
                Action = action,
                Details = details,
                IpAddress = ip,
                Timestamp = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            // Audit failures must not crash the main flow
            _logger.LogError(ex, "Failed to write audit log: {Action} for {User}", action, userId);
        }
    }
}

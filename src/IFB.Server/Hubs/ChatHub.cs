using IFB.Server.Data;
using IFB.Server.Services;
using IFB.Shared.Contracts;
using IFB.Shared.DTOs;
using IFB.Shared.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace IFB.Server.Hubs;

/// <summary>
/// Real-time chat hub — handles send/edit/delete/react/typing/presence.
/// All methods require authentication. The JWT user ID is extracted from claims.
/// </summary>
[Authorize]
public sealed class ChatHub : Hub<IClientHub>, IServerHub
{
    private readonly IMessageService _messages;
    private readonly IConversationService _conversations;
    private readonly IAuditService _audit;
    private readonly IPresenceTracker _presence;
    private readonly AppDbContext _db;
    private readonly ILogger<ChatHub> _logger;

    public ChatHub(
        IMessageService messages,
        IConversationService conversations,
        IAuditService audit,
        IPresenceTracker presence,
        AppDbContext db,
        ILogger<ChatHub> logger)
    {
        _messages = messages;
        _conversations = conversations;
        _audit = audit;
        _presence = presence;
        _db = db;
        _logger = logger;
    }

    private string UserId =>
        Context.User!.FindFirst(ClaimTypes.NameIdentifier)!.Value;

    // ClaimTypes.Name is mapped from the token's unique_name (the username), so read the
    // current display name from the database instead.
    private async Task<string> GetDisplayNameAsync(string userId) =>
        await _db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.DisplayName).FirstOrDefaultAsync()
        ?? "Unknown";

    // ── Connection Lifecycle ──────────────────────────────────────────────────

    public override async Task OnConnectedAsync()
    {
        var userId = UserId;
        _presence.UserConnected(userId, Context.ConnectionId);

        // Join SignalR groups for all conversations the user belongs to
        var convIds = await _db.ConversationMembers
            .Where(m => m.UserId == userId)
            .Select(m => m.ConversationId)
            .ToListAsync();

        foreach (var convId in convIds)
            await Groups.AddToGroupAsync(Context.ConnectionId, convId);

        // Broadcast presence change to all contacts
        await BroadcastPresenceAsync(userId, PresenceStatus.Online);

        // Flush any offline messages
        await DeliverOfflineMessagesAsync(userId);

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = UserId;
        _presence.UserDisconnected(userId, Context.ConnectionId);

        // Only mark offline if no other connections remain
        if (!_presence.IsOnline(userId))
            await BroadcastPresenceAsync(userId, PresenceStatus.Offline);

        await base.OnDisconnectedAsync(exception);
    }

    // ── IServerHub Implementation ─────────────────────────────────────────────

    public async Task SendMessageAsync(SendMessageDto dto)
    {
        var userId = UserId;

        // Validate membership
        if (!await _conversations.IsMemberAsync(dto.ConversationId, userId))
        {
            throw new HubException("Not a member of this conversation.");
        }

        // Validate content
        if (string.IsNullOrWhiteSpace(dto.Content) && dto.FileId == null)
        {
            throw new HubException("Message content required.");
        }

        var (msg, msgDto) = await _messages.SaveMessageAsync(
            dto.ConversationId, userId,
            dto.Content ?? string.Empty,
            dto.Type, dto.ReplyToId, dto.FileId);

        // Broadcast to everyone in the conversation group
        await Clients.Group(dto.ConversationId).MessageReceivedAsync(msgDto);

        // Queue for any offline members
        await QueueForOfflineMembersAsync(dto.ConversationId, msg.Id, userId);

        await _audit.LogAsync(userId, "SendMessage",
            $"conv={dto.ConversationId} msgId={msg.Id}",
            Context.GetHttpContext()?.Connection.RemoteIpAddress?.ToString());
    }

    public async Task EditMessageAsync(EditMessageDto dto)
    {
        var userId = UserId;
        var message = await _db.Messages.FindAsync(dto.MessageId);

        if (message == null || message.SenderId != userId || message.IsDeleted ||
            !await _conversations.IsMemberAsync(message.ConversationId, userId))
        {
            await Clients.Caller.ErrorAsync("Cannot edit this message.");
            return;
        }

        if ((DateTime.UtcNow - message.SentAt).TotalMinutes > 15)
        {
            await Clients.Caller.ErrorAsync("Editing window expired (15 minutes).");
            return;
        }

        if (string.IsNullOrWhiteSpace(dto.NewContent) || dto.NewContent.Length > 10_000)
            throw new HubException("Message must contain 1 to 10,000 characters.");
        message.Content = MessageService.SanitizeContent(dto.NewContent);
        message.IsEdited = true;
        message.EditedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        await Clients.Group(message.ConversationId)
            .MessageEditedAsync(new MessageEditedDto(message.Id, message.ConversationId,
                message.Content, message.EditedAt.Value));
    }

    public async Task DeleteMessageAsync(DeleteMessageDto dto)
    {
        var userId = UserId;
        var message = await _db.Messages
            .Include(m => m.Conversation).ThenInclude(c => c.Members)
            .FirstOrDefaultAsync(m => m.Id == dto.MessageId);

        if (message == null) return;

        var isConvAdmin = message.Conversation.Members
            .Any(m => m.UserId == userId && m.IsAdmin);

        if (!await _conversations.IsMemberAsync(message.ConversationId, userId) ||
            (message.SenderId != userId && !isConvAdmin))
        {
            await Clients.Caller.ErrorAsync("Cannot delete this message.");
            return;
        }

        message.IsDeleted = true;
        message.Content = "[This message was deleted]";
        message.EditedAt = DateTime.UtcNow;
        var orphanedFile = await MessageMaintenance.DetachAttachmentAsync(_db, message);
        await _db.SaveChangesAsync();
        MessageMaintenance.DeleteStoredFile(orphanedFile, _logger);

        await Clients.Group(message.ConversationId)
            .MessageDeletedAsync(new MessageDeletedDto(message.Id, message.ConversationId));
    }

    public async Task ToggleReactionAsync(ToggleReactionDto dto)
    {
        if (!Enum.IsDefined((ReactionEmoji)dto.Emoji)) throw new HubException("Invalid reaction.");
        var userId = UserId;
        var message = await _db.Messages.FindAsync(dto.MessageId);
        if (message == null || message.IsDeleted) return;

        if (!await _conversations.IsMemberAsync(message.ConversationId, userId)) return;

        var existing = await _db.MessageReactions.FindAsync(dto.MessageId, userId, dto.Emoji);
        bool added;
        if (existing != null)
        {
            _db.MessageReactions.Remove(existing);
            added = false;
        }
        else
        {
            _db.MessageReactions.Add(new Models.MessageReaction
            {
                MessageId = dto.MessageId,
                UserId = userId,
                Emoji = dto.Emoji,
                CreatedAt = DateTime.UtcNow
            });
            added = true;
        }
        await _db.SaveChangesAsync();

        await Clients.Group(message.ConversationId)
            .ReactionUpdatedAsync(new ReactionUpdatedDto(
                dto.MessageId, message.ConversationId,
                userId, dto.Emoji, added));
    }

    public async Task MarkReadAsync(MarkReadDto dto)
    {
        var userId = UserId;
        var message = await _db.Messages.FindAsync(dto.MessageId);
        if (message == null || !await _conversations.IsMemberAsync(message.ConversationId, userId)) return;

        // Marking a message read also marks every earlier incoming message, so clients send one
        // call for the newest visible message instead of one call per message.
        var inserted = await MessageMaintenance.MarkReadThroughAsync(_db, message, userId);
        if (inserted == 0) return;

        // One event: clients treat it as "read up to and including this message".
        await Clients.Group(message.ConversationId)
            .ReadReceiptAsync(new ReadReceiptDto(dto.MessageId, message.ConversationId,
                userId, DateTime.UtcNow));
    }

    public async Task SendTypingAsync(TypingDto dto)
    {
        var userId = UserId;
        if (!await _conversations.IsMemberAsync(dto.ConversationId, userId)) return;

        // Broadcast to group but exclude the caller
        await Clients.GroupExcept(dto.ConversationId, Context.ConnectionId)
            .TypingAsync(new TypingEventDto(dto.ConversationId, userId, await GetDisplayNameAsync(userId), dto.IsTyping));
    }

    public async Task UpdatePresenceAsync(UpdatePresenceDto dto)
    {
        if (!Enum.IsDefined(dto.Status)) throw new HubException("Invalid presence status.");
        var userId = UserId;
        _presence.SetStatus(userId, dto.Status);

        // Update DB
        var user = await _db.Users.FindAsync(userId);
        if (user != null)
        {
            user.Presence = dto.Status;
            await _db.SaveChangesAsync();
        }

        await BroadcastPresenceAsync(userId, dto.Status);
    }

    public async Task JoinConversationAsync(string conversationId)
    {
        var userId = UserId;
        if (!await _conversations.IsMemberAsync(conversationId, userId)) return;
        await Groups.AddToGroupAsync(Context.ConnectionId, conversationId);
    }

    public Task LeaveConversationAsync(string conversationId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, conversationId);

    // ── Private Helpers ───────────────────────────────────────────────────────

    private async Task BroadcastPresenceAsync(string userId, PresenceStatus status)
    {
        await _db.Users.Where(u => u.Id == userId).ExecuteUpdateAsync(s => s.SetProperty(u => u.Presence, status));
        // Find all conversations this user is in and notify all members
        var convIds = await _db.ConversationMembers
            .Where(m => m.UserId == userId)
            .Select(m => m.ConversationId)
            .ToListAsync();

        var presenceDto = new PresenceUpdateDto(userId, status, DateTime.UtcNow);
        foreach (var convId in convIds)
            await Clients.Group(convId).PresenceChangedAsync(presenceDto);
    }

    private async Task DeliverOfflineMessagesAsync(string userId)
    {
        while (!Context.ConnectionAborted.IsCancellationRequested)
        {
        var pending = await _db.OfflineMessages
            .Where(o => o.RecipientUserId == userId && !o.IsDelivered)
            .OrderBy(o => o.QueuedAt)
            .Take(100)
            .ToListAsync();

        foreach (var offline in pending)
        {
            var msg = await _db.Messages
                .Include(m => m.Sender)
                .FirstOrDefaultAsync(m => m.Id == offline.MessageId);

            if (msg != null && await _conversations.IsMemberAsync(msg.ConversationId, userId))
            {
                var dto = new MessageDto(
                    msg.Id, msg.ConversationId, msg.SenderId,
                    msg.Sender?.DisplayName ?? "", msg.Sender?.AvatarPath,
                    msg.Content, msg.Type, msg.ReplyToId, msg.FileId,
                    msg.IsEdited, msg.SentAt, []);
                await Clients.Caller.MessageReceivedAsync(dto);
            }
            offline.IsDelivered = true;
        }

        if (pending.Count == 0) break;
        await _db.SaveChangesAsync();
        }
    }

    private async Task QueueForOfflineMembersAsync(
        string conversationId, string messageId, string senderId)
    {
        var members = await _db.ConversationMembers
            .Where(m => m.ConversationId == conversationId && m.UserId != senderId)
            .Select(m => m.UserId)
            .ToListAsync();
        var offlineMembers = members.Where(uid => !_presence.IsOnline(uid)).ToList();

        foreach (var recipientId in offlineMembers)
        {
            _db.OfflineMessages.Add(new Models.OfflineMessage
            {
                Id = Guid.NewGuid().ToString(),
                RecipientUserId = recipientId,
                MessageId = messageId,
                ConversationId = conversationId,
                QueuedAt = DateTime.UtcNow
            });
        }

        if (offlineMembers.Count > 0)
            await _db.SaveChangesAsync();
    }
}

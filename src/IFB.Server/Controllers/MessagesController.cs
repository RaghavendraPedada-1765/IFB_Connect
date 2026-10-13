using IFB.Server.Data;
using IFB.Shared.Contracts;
using IFB.Shared.DTOs;
using IFB.Shared.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using IFB.Server.Hubs;

namespace IFB.Server.Controllers;

/// <summary>
/// Message history, pagination, edit, delete, reactions, read receipts.
/// </summary>
[ApiController]
[Route("api/v1/conversations/{conversationId}/messages")]
[Authorize]
public sealed class MessagesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILogger<MessagesController> _logger;
    private readonly IHubContext<ChatHub, IClientHub> _hub;

    public MessagesController(AppDbContext db, ILogger<MessagesController> logger, IHubContext<ChatHub, IClientHub> hub)
    {
        _db = db;
        _logger = logger;
        _hub = hub;
    }

    private string CurrentUserId =>
        User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value;

    private async Task<bool> IsMemberAsync(string conversationId, string userId) =>
        await _db.ConversationMembers
            .AnyAsync(m => m.ConversationId == conversationId && m.UserId == userId && !m.Conversation.IsArchived);

    // GET /api/v1/conversations/{cid}/messages?before=<utc>&limit=50
    [HttpGet]
    public async Task<IActionResult> GetHistory(
        string conversationId,
        [FromQuery] DateTime? before,
        [FromQuery] int limit = 50, [FromQuery] string? q = null, [FromQuery] string? beforeId = null)
    {
        limit = Math.Clamp(limit, 1, 100);
        var userId = CurrentUserId;

        if (!await IsMemberAsync(conversationId, userId)) return Forbid();

        var cursor = before ?? DateTime.UtcNow;

        var query = _db.Messages.Where(m => m.ConversationId == conversationId &&
            (m.SentAt < cursor || (beforeId != null && m.SentAt == cursor && string.Compare(m.Id, beforeId) < 0)));
        if (!string.IsNullOrWhiteSpace(q)) query = query.Where(m => !m.IsDeleted && m.Content.Contains(q));
        var messages = await query
            .OrderByDescending(m => m.SentAt)
            .ThenByDescending(m => m.Id)
            .Take(limit)
            .Include(m => m.Sender)
            .Include(m => m.Reactions)
            .Include(m => m.ReadReceipts)
            .AsNoTracking()
            .ToListAsync();

        // Return in ascending order (oldest first in page)
        var dtos = messages
            .OrderBy(m => m.SentAt)
            .ThenBy(m => m.Id)
            .Select(m => ToMessageDto(m, userId))
            .ToList();

        // Mark as read up to the newest message in this batch
        var newest = messages.MaxBy(m => m.SentAt);
        if (newest != null)
        {
            var unreadIds = await _db.Messages.Where(m => m.ConversationId == conversationId && m.SentAt <= newest.SentAt &&
                m.SenderId != userId && !m.ReadReceipts.Any(r => r.UserId == userId)).Select(m => m.Id).ToListAsync();
            foreach (var messageId in unreadIds)
            {
                _db.MessageReadReceipts.Add(new Models.MessageReadReceipt
                {
                    MessageId = messageId,
                    UserId = userId,
                    ReadAt = DateTime.UtcNow
                });
            }
            await _db.SaveChangesAsync();
            foreach (var messageId in unreadIds)
                await _hub.Clients.Group(conversationId).ReadReceiptAsync(new ReadReceiptDto(messageId, conversationId, userId, DateTime.UtcNow));
        }

        return Ok(new { messages = dtos, hasMore = dtos.Count == limit });
    }

    // POST /api/v1/conversations/{cid}/messages/read
    [HttpPost("read")]
    public async Task<IActionResult> MarkRead(string conversationId, [FromBody] MarkReadDto dto)
    {
        var userId = CurrentUserId;
        if (!await IsMemberAsync(conversationId, userId)) return Forbid();
        if (!await _db.Messages.AnyAsync(m => m.Id == dto.MessageId && m.ConversationId == conversationId)) return NotFound();

        // Upsert read receipt
        var receipt = await _db.MessageReadReceipts
            .FirstOrDefaultAsync(r => r.MessageId == dto.MessageId && r.UserId == userId);

        if (receipt == null)
        {
            _db.MessageReadReceipts.Add(new Models.MessageReadReceipt
            {
                MessageId = dto.MessageId,
                UserId = userId,
                ReadAt = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();
        }

        await _hub.Clients.Group(conversationId).ReadReceiptAsync(new ReadReceiptDto(dto.MessageId, conversationId, userId, DateTime.UtcNow));
        return NoContent();
    }

    // PATCH /api/v1/conversations/{cid}/messages/{id}
    [HttpPatch("{id}")]
    public async Task<IActionResult> Edit(string conversationId, string id,
        [FromBody] EditContentDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.NewContent) || dto.NewContent.Length > 10_000)
            return BadRequest("Content cannot be empty.");

        var userId = CurrentUserId;
        if (!await IsMemberAsync(conversationId, userId)) return Forbid();
        var message = await _db.Messages
            .FirstOrDefaultAsync(m => m.Id == id && m.ConversationId == conversationId);

        if (message == null) return NotFound();
        if (message.SenderId != userId) return Forbid();
        if (message.IsDeleted) return BadRequest("Cannot edit a deleted message.");

        if ((DateTime.UtcNow - message.SentAt).TotalMinutes > 15)
            return UnprocessableEntity(new { error = "Message can only be edited within 15 minutes." });

        message.Content = dto.NewContent.Trim();
        message.IsEdited = true;
        message.EditedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        await _hub.Clients.Group(conversationId).MessageEditedAsync(new MessageEditedDto(id, conversationId, message.Content, message.EditedAt.Value));
        return Ok(ToMessageDto(message, userId));
    }

    // DELETE /api/v1/conversations/{cid}/messages/{id}
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string conversationId, string id)
    {
        var userId = CurrentUserId;
        if (!await IsMemberAsync(conversationId, userId)) return Forbid();
        var message = await _db.Messages
            .FirstOrDefaultAsync(m => m.Id == id && m.ConversationId == conversationId);

        if (message == null) return NotFound();

        // Owner can always delete their own; admins can delete anything in the conv
        var isAdmin = await _db.ConversationMembers
            .AnyAsync(m => m.ConversationId == conversationId && m.UserId == userId && m.IsAdmin);

        if (message.SenderId != userId && !isAdmin) return Forbid();

        // Soft delete — replace content with tombstone
        message.IsDeleted = true;
        message.Content = "[This message was deleted]";
        message.EditedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _hub.Clients.Group(conversationId).MessageDeletedAsync(new MessageDeletedDto(id, conversationId));

        return NoContent();
    }

    // POST /api/v1/conversations/{cid}/messages/{id}/react
    [HttpPost("{id}/react")]
    public async Task<IActionResult> React(string conversationId, string id,
        [FromBody] ReactDto dto)
    {
        if (!Enum.IsDefined(typeof(ReactionEmoji), dto.Emoji)) return BadRequest("Invalid reaction.");
        var userId = CurrentUserId;
        if (!await IsMemberAsync(conversationId, userId)) return Forbid();
        if (!await _db.Messages.AnyAsync(m => m.Id == id && m.ConversationId == conversationId && !m.IsDeleted)) return NotFound();

        var existing = await _db.MessageReactions
            .FirstOrDefaultAsync(r => r.MessageId == id && r.UserId == userId && r.Emoji == dto.Emoji);

        if (existing != null)
        {
            // Toggle off
            _db.MessageReactions.Remove(existing);
        }
        else
        {
            _db.MessageReactions.Add(new Models.MessageReaction
            {
                MessageId = id,
                UserId = userId,
                Emoji = dto.Emoji,
                CreatedAt = DateTime.UtcNow
            });
        }

        await _db.SaveChangesAsync();
        await _hub.Clients.Group(conversationId).ReactionUpdatedAsync(new ReactionUpdatedDto(id, conversationId, userId, dto.Emoji, existing == null));
        return NoContent();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static MessageDto ToMessageDto(Models.Message m, string currentUserId)
    {
        var reactions = m.Reactions?
            .GroupBy(r => r.Emoji)
            .Select(g => new ReactionDto(g.Key, g.Count(),
                g.Any(r => r.UserId == currentUserId)))
            .ToList() ?? [];

        return new MessageDto(
            m.Id, m.ConversationId, m.SenderId,
            m.Sender?.DisplayName ?? "Unknown",
            m.Sender?.AvatarPath,
            m.Content, m.Type, m.ReplyToId, m.FileId,
            m.IsEdited, m.SentAt, reactions, m.IsDeleted, m.ReadReceipts.Any(r => r.UserId != m.SenderId));
    }
}

// Local controller DTOs (server-side only, not shared)
// MarkReadDto, EditMessageDto, ReactDto are in IFB.Shared.DTOs
// ─────────────────────────────────────────────────────────────
public record EditContentDto(string NewContent);

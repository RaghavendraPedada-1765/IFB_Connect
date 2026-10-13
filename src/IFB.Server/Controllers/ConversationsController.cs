using IFB.Server.Data;
using IFB.Server.Models;
using IFB.Shared.DTOs;
using IFB.Shared.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using IFB.Server.Hubs;
using IFB.Server.Services;
using IFB.Shared.Contracts;

namespace IFB.Server.Controllers;

/// <summary>
/// Conversation management: list, create DM, create group, members, archive.
/// All endpoints require authentication.
/// </summary>
[ApiController]
[Route("api/v1/conversations")]
[Authorize]
public sealed class ConversationsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILogger<ConversationsController> _logger;
    private readonly IHubContext<ChatHub, IClientHub> _hub;
    private readonly IPresenceTracker _presence;

    public ConversationsController(AppDbContext db, ILogger<ConversationsController> logger,
        IHubContext<ChatHub, IClientHub> hub, IPresenceTracker presence)
    {
        _db = db;
        _logger = logger;
        _hub = hub; _presence = presence;
    }

    private string CurrentUserId =>
        User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value;

    // GET /api/v1/conversations
    [HttpGet]
    public async Task<IActionResult> GetMyConversations()
    {
        var userId = CurrentUserId;

        var conversations = await _db.ConversationMembers
            .Where(m => m.UserId == userId)
            .Include(m => m.Conversation)
                .ThenInclude(c => c.Members)
                    .ThenInclude(m => m.User)
            .Select(m => m.Conversation)
            .Include(c => c.Messages.OrderByDescending(m => m.SentAt).Take(1)).ThenInclude(m => m.Sender)
            .Where(c => !c.IsArchived)
            .OrderByDescending(c => c.LastActivity)
            .AsNoTracking()
            .ToListAsync();

        var unread = await _db.Messages.Where(m => !m.IsDeleted && m.SenderId != userId &&
            m.Conversation.Members.Any(cm => cm.UserId == userId) && !m.ReadReceipts.Any(r => r.UserId == userId))
            .GroupBy(m => m.ConversationId).Select(g => new { Id = g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Id, g => g.Count);
        var dtos = conversations.Select(c => ToConversationDto(c, userId) with { UnreadCount = unread.GetValueOrDefault(c.Id) }).ToList();
        return Ok(dtos);
    }

    // GET /api/v1/conversations/{id}
    [HttpGet("{id}")]
    public async Task<IActionResult> GetConversation(string id)
    {
        var userId = CurrentUserId;

        var conv = await _db.Conversations
            .Include(c => c.Members).ThenInclude(m => m.User)
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id);

        if (conv == null) return NotFound();
        if (!conv.Members.Any(m => m.UserId == userId)) return Forbid();

        return Ok(ToConversationDto(conv, userId));
    }

    // POST /api/v1/conversations/dm
    [HttpPost("dm")]
    public async Task<IActionResult> CreateOrGetDm([FromBody] CreateDmDto dto)
    {
        var userId = CurrentUserId;
        if (userId == dto.OtherUserId) return BadRequest("Cannot create DM with yourself.");

        // Check for existing DM
        var existing = await _db.Conversations
            .Include(c => c.Members).ThenInclude(m => m.User)
            .Where(c => c.Type == ConversationType.DirectMessage)
            .Where(c => c.Members.Any(m => m.UserId == userId))
            .Where(c => c.Members.Any(m => m.UserId == dto.OtherUserId))
            .AsNoTracking()
            .FirstOrDefaultAsync();

        if (existing != null) return Ok(ToConversationDto(existing, userId));

        // Ensure other user exists
        var other = await _db.Users.FindAsync(dto.OtherUserId);
        if (other == null || !other.IsActive) return NotFound("User not found.");

        var conv = new Conversation
        {
            Id = Guid.NewGuid().ToString(),
            Type = ConversationType.DirectMessage,
            Name = other.DisplayName,
            CreatedByUserId = userId,
            CreatedAt = DateTime.UtcNow,
            LastActivity = DateTime.UtcNow,
            RetentionDays = 90
        };

        conv.Members.Add(new ConversationMember { UserId = userId, JoinedAt = DateTime.UtcNow });
        conv.Members.Add(new ConversationMember { UserId = dto.OtherUserId, JoinedAt = DateTime.UtcNow });

        _db.Conversations.Add(conv);
        await _db.SaveChangesAsync();

        _logger.LogInformation("DM created between {A} and {B}", userId, dto.OtherUserId);
        await NotifyMembersAsync(conv.Id);
        return CreatedAtAction(nameof(GetConversation), new { id = conv.Id },
            ToConversationDto(conv, userId));
    }

    // POST /api/v1/conversations/group
    [HttpPost("group")]
    public async Task<IActionResult> CreateGroup([FromBody] CreateGroupDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name)) return BadRequest("Name required.");
        if (dto.MemberIds == null || dto.MemberIds.Count < 1 || dto.MemberIds.Count > 500 || dto.Name.Length > 128) return BadRequest("Add 1–500 members and a name of at most 128 characters.");

        var userId = CurrentUserId;
        var allMemberIds = dto.MemberIds.Distinct().Append(userId).ToHashSet();

        // Validate all members exist
        var validCount = await _db.Users.CountAsync(u => allMemberIds.Contains(u.Id) && u.IsActive);
        if (validCount != allMemberIds.Count) return BadRequest("One or more users not found.");

        var conv = new Conversation
        {
            Id = Guid.NewGuid().ToString(),
            Type = ConversationType.Group,
            Name = dto.Name.Trim(),
            Description = dto.Description?.Trim(),
            CreatedByUserId = userId,
            CreatedAt = DateTime.UtcNow,
            LastActivity = DateTime.UtcNow,
            RetentionDays = 90
        };

        foreach (var memberId in allMemberIds)
            conv.Members.Add(new ConversationMember
            {
                UserId = memberId,
                IsAdmin = memberId == userId,
                JoinedAt = DateTime.UtcNow
            });

        _db.Conversations.Add(conv);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Group '{Name}' created by {User}", conv.Name, userId);
        await NotifyMembersAsync(conv.Id);
        return CreatedAtAction(nameof(GetConversation), new { id = conv.Id },
            ToConversationDto(conv, userId));
    }

    // DELETE /api/v1/conversations/{id}/leave
    [HttpDelete("{id}/leave")]
    public async Task<IActionResult> Leave(string id)
    {
        var userId = CurrentUserId;
        var member = await _db.ConversationMembers
            .FirstOrDefaultAsync(m => m.ConversationId == id && m.UserId == userId);

        if (member == null) return NotFound();
        if (member.IsAdmin && !await _db.ConversationMembers.AnyAsync(m => m.ConversationId == id && m.UserId != userId && m.IsAdmin))
            return BadRequest("The last conversation administrator cannot leave. Ask a global administrator to archive the conversation.");
        _db.ConversationMembers.Remove(member);
        await _db.SaveChangesAsync();
        foreach (var connection in _presence.GetConnectionIds(userId))
            await _hub.Groups.RemoveFromGroupAsync(connection, id);
        return NoContent();
    }

    // GET /api/v1/conversations/{id}/members
    [HttpGet("{id}/members")]
    public async Task<IActionResult> GetMembers(string id)
    {
        var userId = CurrentUserId;
        var isMember = await _db.ConversationMembers
            .AnyAsync(m => m.ConversationId == id && m.UserId == userId);
        if (!isMember && !User.IsInRole("Admin")) return Forbid();

        var members = await _db.ConversationMembers
            .Where(m => m.ConversationId == id)
            .Include(m => m.User)
            .AsNoTracking()
            .Select(m => new MemberDto(
                m.UserId,
                m.User.DisplayName,
                m.User.AvatarPath,
                m.IsAdmin,
                m.User.Presence,
                m.JoinedAt))
            .ToListAsync();

        return Ok(members);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task NotifyMembersAsync(string id)
    {
        var conv = await _db.Conversations.Include(c => c.Members).ThenInclude(m => m.User).FirstAsync(c => c.Id == id);
        foreach (var member in conv.Members)
        {
            foreach (var connection in _presence.GetConnectionIds(member.UserId)) await _hub.Groups.AddToGroupAsync(connection, id);
            await _hub.Clients.User(member.UserId).ConversationUpdatedAsync(ToConversationDto(conv, member.UserId));
        }
    }

    [HttpGet("channels")]
    public async Task<IActionResult> Channels() => Ok(await _db.Conversations
        .Where(c => c.Type == ConversationType.Channel && !c.IsArchived)
        .Select(c => new { c.Id, c.Name, c.Description, Joined = c.Members.Any(m => m.UserId == CurrentUserId) }).ToListAsync());

    [HttpPost("{id}/join")]
    public async Task<IActionResult> Join(string id)
    {
        if (!await _db.Conversations.AnyAsync(c => c.Id == id && c.Type == ConversationType.Channel && !c.IsArchived)) return NotFound();
        if (!await _db.ConversationMembers.AnyAsync(m => m.ConversationId == id && m.UserId == CurrentUserId))
        { _db.ConversationMembers.Add(new ConversationMember { ConversationId = id, UserId = CurrentUserId, JoinedAt = DateTime.UtcNow }); await _db.SaveChangesAsync(); }
        await NotifyMembersAsync(id); return NoContent();
    }

    [HttpPost("{id}/members/{userId}")]
    public async Task<IActionResult> AddMember(string id, string userId)
    {
        if (!User.IsInRole("Admin") && !await _db.ConversationMembers.AnyAsync(m => m.ConversationId == id && m.UserId == CurrentUserId && m.IsAdmin)) return Forbid();
        var conv = await _db.Conversations.FindAsync(id);
        if (conv == null || conv.Type == ConversationType.DirectMessage) return BadRequest();
        if (!await _db.Users.AnyAsync(u => u.Id == userId && u.IsActive)) return NotFound();
        if (!await _db.ConversationMembers.AnyAsync(m => m.ConversationId == id && m.UserId == userId))
        { _db.ConversationMembers.Add(new ConversationMember { ConversationId = id, UserId = userId }); await _db.SaveChangesAsync(); }
        await NotifyMembersAsync(id); return NoContent();
    }

    [HttpDelete("{id}/members/{userId}")]
    public async Task<IActionResult> RemoveMember(string id, string userId)
    {
        if (!User.IsInRole("Admin") && !await _db.ConversationMembers.AnyAsync(m => m.ConversationId == id && m.UserId == CurrentUserId && m.IsAdmin)) return Forbid();
        var member = await _db.ConversationMembers.Include(m => m.Conversation).FirstOrDefaultAsync(m => m.ConversationId == id && m.UserId == userId);
        if (member == null) return NotFound();
        if (member.Conversation.Type == ConversationType.DirectMessage) return BadRequest();
        if (member.IsAdmin && !await _db.ConversationMembers.AnyAsync(m => m.ConversationId == id && m.UserId != userId && m.IsAdmin))
            return BadRequest("Assign another conversation administrator before removing the last administrator.");
        _db.ConversationMembers.Remove(member); await _db.SaveChangesAsync();
        foreach (var connection in _presence.GetConnectionIds(userId)) await _hub.Groups.RemoveFromGroupAsync(connection, id);
        return NoContent();
    }

    private static ConversationDto ToConversationDto(Conversation c, string currentUserId)
    {
        var lastMsg = c.Messages?.OrderByDescending(m => m.SentAt).FirstOrDefault();
        var members = c.Members?.Select(m => new UserDto(
            m.UserId,
            m.User?.DisplayName ?? "",
            m.User?.Email ?? "",
            m.User?.Title,
            m.User?.Department,
            m.User?.AvatarPath,
            m.User?.Role ?? UserRole.Employee,
            m.User?.Presence ?? PresenceStatus.Offline)).ToList() ?? [];

        return new ConversationDto(
            c.Id,
            c.Type,
            c.Type == ConversationType.DirectMessage ? c.Members?.FirstOrDefault(m => m.UserId != currentUserId)?.User?.DisplayName ?? c.Name : c.Name,
            c.Description,
            c.IconPath,
            members,
            lastMsg == null ? null : new MessageDto(
                lastMsg.Id, lastMsg.ConversationId, lastMsg.SenderId,
                lastMsg.Sender?.DisplayName ?? "", lastMsg.Sender?.AvatarPath,
                lastMsg.Content, lastMsg.Type, lastMsg.ReplyToId, lastMsg.FileId,
                lastMsg.IsEdited, lastMsg.SentAt, []),
            c.Messages?.Count(m => !m.IsDeleted) ?? 0,
            c.LastActivity);
    }
}

// ── Small request DTOs not in shared ─────────────────────────────────────────
public record CreateDmDto(string OtherUserId);
public record CreateGroupDto(string Name, string? Description, List<string> MemberIds);
public record MemberDto(string Id, string DisplayName, string? AvatarUrl,
    bool IsAdmin, PresenceStatus Presence, DateTime JoinedAt);


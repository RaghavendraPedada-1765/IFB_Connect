using IFB.Server.Auth;
using IFB.Server.Data;
using IFB.Server.Hubs;
using IFB.Server.Models;
using IFB.Server.Services;
using IFB.Shared.Contracts;
using IFB.Shared.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace IFB.Server.Controllers;

[ApiController, Route("api/v1/admin"), Authorize(Policy = "AdminOnly")]
public sealed class AdminController(AppDbContext db, IPresenceTracker presence,
    IHubContext<ChatHub, IClientHub> hub) : ControllerBase
{
    [HttpGet("users")]
    public async Task<IActionResult> Users() => Ok(await db.Users.OrderBy(u => u.DisplayName)
        .Select(u => new { u.Id, u.Username, u.DisplayName, u.Email, u.Department, u.Title, u.Role, u.IsActive }).ToListAsync());

    [HttpGet("connections")]
    public IActionResult Connections() => Ok(presence.GetOnlineUserIds().Select(id => new
        { UserId = id, Connections = presence.GetConnectionIds(id).Count, Status = presence.GetStatus(id) }));

    [HttpPost("users/{id}/logout")]
    public async Task<IActionResult> ForceLogout(string id)
    {
        if (!await db.Users.AnyAsync(u => u.Id == id)) return NotFound();
        await db.Users.Where(u => u.Id == id).ExecuteUpdateAsync(s => s.SetProperty(u => u.TokenVersion, u => u.TokenVersion + 1));
        await db.RefreshTokens.Where(t => t.UserId == id).ExecuteUpdateAsync(s => s.SetProperty(t => t.IsRevoked, true));
        await hub.Clients.User(id).UserForceLogoutAsync("Your session was ended by an administrator.");
        foreach (var cid in await db.ConversationMembers.Where(m => m.UserId == id).Select(m => m.ConversationId).ToListAsync())
            foreach (var connection in presence.GetConnectionIds(id)) await hub.Groups.RemoveFromGroupAsync(connection, cid);
        return NoContent();
    }

    [HttpPatch("users/{id}")]
    public async Task<IActionResult> UpdateUser(string id, AdminUserUpdate dto)
    {
        var user = await db.Users.FindAsync(id);
        if (user == null) return NotFound();
        if (!Enum.IsDefined(dto.Role)) return BadRequest();
        if (id == User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value && (!dto.IsActive || dto.Role != UserRole.Admin))
            return BadRequest("You cannot remove your own administrator access.");
        user.Role = dto.Role; user.IsActive = dto.IsActive; user.Department = dto.Department; user.Title = dto.Title;
        if (dto.NewPassword != null)
        {
            if (!user.IsLocalAccount || dto.NewPassword.Length < 12) return BadRequest("Local password must have at least 12 characters.");
            (user.PasswordHash, user.PasswordSalt) = AuthService.HashPassword(dto.NewPassword);
        }
        await db.SaveChangesAsync(); return await ForceLogout(id);
    }

    [HttpGet("conversations")]
    public async Task<IActionResult> Conversations() => Ok(await db.Conversations.OrderBy(c => c.Name)
        .Select(c => new { c.Id, c.Name, c.Type, c.Description, c.RetentionDays, c.IsArchived, Members = c.Members.Count }).ToListAsync());

    [HttpPost("channels")]
    public async Task<IActionResult> CreateChannel(ChannelRequest dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name) || dto.Name.Length > 128 || dto.RetentionDays < 1 || dto.RetentionDays > 3650) return BadRequest();
        var id = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value;
        var channel = new Conversation { Name = dto.Name.Trim(), Description = dto.Description,
            Type = ConversationType.Channel, RetentionDays = dto.RetentionDays, CreatedByUserId = id };
        channel.Members.Add(new ConversationMember { UserId = id, IsAdmin = true });
        db.Conversations.Add(channel); await db.SaveChangesAsync(); return Ok(new { channel.Id });
    }

    [HttpPatch("conversations/{id}")]
    public async Task<IActionResult> UpdateConversation(string id, ConversationUpdate dto)
    {
        if (dto.RetentionDays < 1 || dto.RetentionDays > 3650 || string.IsNullOrWhiteSpace(dto.Name) || dto.Name.Length > 128) return BadRequest();
        var conversation = await db.Conversations.FindAsync(id); if (conversation == null) return NotFound();
        conversation.Name = dto.Name.Trim(); conversation.RetentionDays = dto.RetentionDays; conversation.IsArchived = dto.IsArchived;
        await db.SaveChangesAsync(); return NoContent();
    }

    [HttpGet("audit")]
    public async Task<IActionResult> Audit([FromQuery] int limit = 200, [FromQuery] int offset = 0) =>
        Ok(await db.AuditLogs.OrderByDescending(l => l.Timestamp).Skip(Math.Max(0, offset)).Take(Math.Clamp(limit, 1, 1000)).AsNoTracking().ToListAsync());

    [HttpGet("audit/export")]
    public async Task<IActionResult> Export([FromQuery] DateTime? since)
    {
        var logs = await db.AuditLogs.Where(l => since == null || l.Timestamp >= since).OrderBy(l => l.Timestamp).Take(100_000).AsNoTracking().ToListAsync();
        static string Cell(string? s)
        {
            s ??= ""; if (s.Length > 0 && "=+-@".Contains(s[0])) s = "'" + s;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }
        var csv = new StringBuilder("Timestamp,UserId,Action,Details,IpAddress\r\n");
        foreach (var l in logs) csv.AppendLine(string.Join(",", Cell(l.Timestamp.ToString("O")), Cell(l.UserId), Cell(l.Action), Cell(l.Details), Cell(l.IpAddress)));
        return File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", "ifb-audit.csv");
    }
}
public record AdminUserUpdate(UserRole Role, bool IsActive, string? Department, string? Title, string? NewPassword);
public record ChannelRequest(string Name, string? Description, int RetentionDays = 90);
public record ConversationUpdate(string Name, int RetentionDays, bool IsArchived);

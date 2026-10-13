using IFB.Server.Data;
using IFB.Server.Models;
using IFB.Shared.DTOs;
using IFB.Shared.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using IFB.Server.Hubs;
using IFB.Shared.Contracts;
using Microsoft.AspNetCore.SignalR;

namespace IFB.Server.Controllers;

/// <summary>
/// Admin announcement broadcasts with priority tiers and acknowledgement tracking.
/// </summary>
[ApiController]
[Route("api/v1/announcements")]
[Authorize]
public sealed class AnnouncementsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILogger<AnnouncementsController> _logger;
    private readonly IHubContext<ChatHub, IClientHub> _hub;

    public AnnouncementsController(AppDbContext db, ILogger<AnnouncementsController> logger, IHubContext<ChatHub, IClientHub> hub)
    {
        _db = db;
        _logger = logger;
        _hub = hub;
    }

    private string CurrentUserId =>
        User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value;

    // GET /api/v1/announcements?active=true
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] bool active = true)
    {
        var query = _db.Announcements.Include(a => a.Author).AsQueryable();

        if (active)
            query = query.Where(a => a.ExpiresAt == null || a.ExpiresAt > DateTime.UtcNow);

        var userId = CurrentUserId;
        var items = await query
            .OrderByDescending(a => a.Priority)
            .ThenByDescending(a => a.PostedAt)
            .AsNoTracking()
            .ToListAsync();

        // Include whether current user has acknowledged each
        var ackedIds = (await _db.AnnouncementAcks
            .Where(a => a.UserId == userId)
            .Select(a => a.AnnouncementId)
            .ToListAsync()).ToHashSet();

        var dtos = items.Select(a => new AnnouncementDto(
            a.Id, a.Title, a.Content,
            a.Author?.DisplayName ?? "Admin",
            a.Priority, a.PostedAt, a.ExpiresAt,
            ackedIds.Contains(a.Id))).ToList();

        return Ok(dtos);
    }

    // POST /api/v1/announcements — admin only
    [HttpPost]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Post([FromBody] PostAnnouncementDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Title) || string.IsNullOrWhiteSpace(dto.Content) ||
            dto.Title.Length > 200 || dto.Content.Length > 10_000 || !Enum.IsDefined(dto.Priority)) return BadRequest("A valid title, message and priority are required.");
        if (string.IsNullOrWhiteSpace(dto.Title) || string.IsNullOrWhiteSpace(dto.Content))
            return BadRequest("Title and content required.");

        var userId = CurrentUserId;
        var ann = new Announcement
        {
            Id = Guid.NewGuid().ToString(),
            Title = dto.Title.Trim(),
            Content = dto.Content.Trim(),
            AuthorId = userId,
            Priority = dto.Priority,
            PostedAt = DateTime.UtcNow,
            ExpiresAt = dto.ExpiresAt
        };

        _db.Announcements.Add(ann);
        await _db.SaveChangesAsync();
        await _hub.Clients.All.AnnouncementPostedAsync(new IFB.Shared.DTOs.AnnouncementDto(
            ann.Id, ann.Title, ann.Content, User.Identity?.Name ?? "Administrator", ann.Priority, ann.PostedAt, ann.ExpiresAt, false));
        _logger.LogInformation("Announcement posted: '{Title}' priority={P}", ann.Title, ann.Priority);

        return CreatedAtAction(nameof(GetAll), null, ann);
    }

    // POST /api/v1/announcements/{id}/acknowledge
    [HttpPost("{id}/acknowledge")]
    public async Task<IActionResult> Acknowledge(string id)
    {
        var userId = CurrentUserId;

        var exists = await _db.Announcements.AnyAsync(a => a.Id == id);
        if (!exists) return NotFound();

        var alreadyAcked = await _db.AnnouncementAcks
            .AnyAsync(a => a.AnnouncementId == id && a.UserId == userId);

        if (!alreadyAcked)
        {
            _db.AnnouncementAcks.Add(new AnnouncementAck
            {
                AnnouncementId = id,
                UserId = userId,
                AcknowledgedAt = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();
        }

        return NoContent();
    }

    // DELETE /api/v1/announcements/{id} — admin only
    [HttpDelete("{id}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Delete(string id)
    {
        var ann = await _db.Announcements.FindAsync(id);
        if (ann == null) return NotFound();
        _db.Announcements.Remove(ann);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // GET /api/v1/announcements/{id}/acks — admin sees who acknowledged
    [HttpGet("{id}/acks")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> GetAcks(string id)
    {
        var acks = await _db.AnnouncementAcks
            .Where(a => a.AnnouncementId == id)
            .Include(a => a.User)
            .AsNoTracking()
            .Select(a => new
            {
                UserId = a.UserId,
                DisplayName = a.User != null ? a.User.DisplayName : "",
                AcknowledgedAt = a.AcknowledgedAt
            })
            .OrderBy(a => a.DisplayName)
            .ToListAsync();

        return Ok(acks);
    }
}

public record PostAnnouncementDto(
    string Title, string Content,
    AnnouncementPriority Priority,
    DateTime? ExpiresAt);

public record AnnouncementDto(
    string Id, string Title, string Content,
    string AuthorName, AnnouncementPriority Priority,
    DateTime PostedAt, DateTime? ExpiresAt,
    bool IsAcknowledged);

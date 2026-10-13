using IFB.Server.Data;
using IFB.Server.Models;
using IFB.Shared.DTOs;
using IFB.Shared.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using IFB.Server.Hubs;
using IFB.Server.Services;
using IFB.Shared.Contracts;
using Microsoft.AspNetCore.SignalR;

namespace IFB.Server.Controllers;

/// <summary>
/// Employee directory, presence lookup, profile updates, admin user management.
/// </summary>
[ApiController]
[Route("api/v1/users")]
[Authorize]
public sealed class UsersController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILogger<UsersController> _logger;
    private readonly IHubContext<ChatHub, IClientHub> _hub;
    private readonly IPresenceTracker _presence;

    public UsersController(AppDbContext db, ILogger<UsersController> logger, IHubContext<ChatHub, IClientHub> hub, IPresenceTracker presence)
    {
        _db = db;
        _logger = logger;
        _hub = hub; _presence = presence;
    }

    private string CurrentUserId =>
        User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value;

    // GET /api/v1/users?q=&dept=&limit=50&offset=0
    [HttpGet]
    public async Task<IActionResult> Search(
        [FromQuery] string? q,
        [FromQuery] string? dept,
        [FromQuery] int limit = 50,
        [FromQuery] int offset = 0)
    {
        limit = Math.Clamp(limit, 1, 200);

        var query = _db.Users.Where(u => u.IsActive).AsQueryable();

        if (!string.IsNullOrWhiteSpace(q))
        {
            var lower = q.ToLower();
            query = query.Where(u =>
                u.DisplayName.ToLower().Contains(lower) ||
                u.Username.ToLower().Contains(lower) ||
                (u.Department != null && u.Department.ToLower().Contains(lower)) ||
                (u.Title != null && u.Title.ToLower().Contains(lower)));
        }

        if (!string.IsNullOrWhiteSpace(dept))
            query = query.Where(u => u.Department == dept);

        var total = await query.CountAsync();
        var users = await query
            .OrderBy(u => u.DisplayName)
            .Skip(offset)
            .Take(limit)
            .AsNoTracking()
            .Select(u => new UserDto(
                u.Id, u.DisplayName, u.Email,
                u.Title, u.Department, u.AvatarPath,
                u.Role, u.Presence))
            .ToListAsync();

        return Ok(new { total, users });
    }

    // GET /api/v1/users/{id}
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id)
    {
        var user = await _db.Users
            .Where(u => u.Id == id && u.IsActive)
            .AsNoTracking()
            .Select(u => new UserDto(
                u.Id, u.DisplayName, u.Email,
                u.Title, u.Department, u.AvatarPath,
                u.Role, u.Presence))
            .FirstOrDefaultAsync();

        return user == null ? NotFound() : Ok(user);
    }

    // GET /api/v1/users/me
    [HttpGet("me")]
    public async Task<IActionResult> GetMe()
    {
        var userId = CurrentUserId;
        var user = await _db.Users
            .Where(u => u.Id == userId)
            .AsNoTracking()
            .Select(u => new UserDto(
                u.Id, u.DisplayName, u.Email,
                u.Title, u.Department, u.AvatarPath,
                u.Role, u.Presence))
            .FirstOrDefaultAsync();

        return user == null ? NotFound() : Ok(user);
    }

    // PATCH /api/v1/users/me/presence
    [HttpPatch("me/presence")]
    public async Task<IActionResult> UpdatePresence([FromBody] UpdatePresenceDto dto)
    {
        var userId = CurrentUserId;
        var user = await _db.Users.FindAsync(userId);
        if (user == null) return NotFound();

        user.Presence = dto.Status;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // PATCH /api/v1/users/me/profile
    [HttpPatch("me/profile")]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileDto dto)
    {
        var userId = CurrentUserId;
        var user = await _db.Users.FindAsync(userId);
        if (user == null) return NotFound();

        if (!string.IsNullOrWhiteSpace(dto.DisplayName))
            user.DisplayName = dto.DisplayName.Trim();
        user.Title = dto.Title?.Trim(); user.Department = dto.Department?.Trim();

        await _db.SaveChangesAsync();
        return NoContent();
    }

    // ── Admin-only ────────────────────────────────────────────────────────────

    // POST /api/v1/users  — create local account
    [HttpPost]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> CreateLocalUser([FromBody] CreateUserDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Username) || string.IsNullOrWhiteSpace(dto.Password))
            return BadRequest("Username and password required.");

        var exists = await _db.Users.AnyAsync(u => u.Username == dto.Username.ToLower());
        if (exists) return Conflict("Username already exists.");

        if (dto.Password.Length < 12 || string.IsNullOrWhiteSpace(dto.DisplayName) ||
            string.IsNullOrWhiteSpace(dto.Email) || !Enum.IsDefined(dto.Role))
            return BadRequest("Name, email, valid role and a password of at least 12 characters are required.");
        if (await _db.Users.AnyAsync(u => u.Email == dto.Email.Trim())) return Conflict("Email already exists.");
        var (hash, salt) = Auth.AuthService.HashPassword(dto.Password);
        var user = new User
        {
            Id = Guid.NewGuid().ToString(),
            Username = dto.Username.ToLower(),
            DisplayName = dto.DisplayName,
            Email = dto.Email,
            Department = dto.Department,
            Title = dto.Title,
            Role = dto.Role,
            IsActive = true,
            IsLocalAccount = true,
            PasswordHash = hash,
            PasswordSalt = salt,
            CreatedAt = DateTime.UtcNow,
            Presence = PresenceStatus.Offline
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        _logger.LogInformation("Admin created local user {Username}", user.Username);

        return CreatedAtAction(nameof(GetById), new { id = user.Id },
            new UserDto(user.Id, user.DisplayName, user.Email,
                user.Title, user.Department, user.AvatarPath,
                user.Role, user.Presence));
    }

    // PATCH /api/v1/users/{id}/disable
    [HttpPatch("{id}/disable")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> DisableUser(string id)
    {
        var user = await _db.Users.FindAsync(id);
        if (user == null) return NotFound();
        if (user.Id == CurrentUserId) return BadRequest("Cannot disable yourself.");

        user.IsActive = false;
        user.TokenVersion++;
        await _db.RefreshTokens.Where(t => t.UserId == id).ExecuteUpdateAsync(s => s.SetProperty(t => t.IsRevoked, true));
        await _db.SaveChangesAsync();
        _logger.LogWarning("Admin disabled user {User}", user.Username);
        await _hub.Clients.User(id).UserForceLogoutAsync("Account disabled by an administrator.");
        foreach (var cid in await _db.ConversationMembers.Where(m => m.UserId == id).Select(m => m.ConversationId).ToListAsync())
            foreach (var connection in _presence.GetConnectionIds(id)) await _hub.Groups.RemoveFromGroupAsync(connection, cid);
        return NoContent();
    }

    // GET /api/v1/users/departments
    [HttpGet("departments")]
    public async Task<IActionResult> GetDepartments()
    {
        var depts = await _db.Users
            .Where(u => u.IsActive && u.Department != null)
            .Select(u => u.Department!)
            .Distinct()
            .OrderBy(d => d)
            .AsNoTracking()
            .ToListAsync();

        return Ok(depts);
    }

}
public record UpdatePresenceDto(PresenceStatus Status);
public record UpdateProfileDto(string? DisplayName, string? Title, string? Department);
public record CreateUserDto(
    string Username, string Password, string DisplayName,
    string Email, string? Department, string? Title, UserRole Role);


using IFB.Shared.Enums;
using System.ComponentModel.DataAnnotations;

namespace IFB.Shared.DTOs;

// ═══════════════════════════════════════════════════════════════════════════════
// Auth
// ═══════════════════════════════════════════════════════════════════════════════

public record LoginRequestDto([Required, MaxLength(128)] string Username, [Required, MaxLength(1024)] string Password);

public record LoginResponseDto(
    string AccessToken,
    string RefreshToken,
    DateTime ExpiresAt,
    UserDto User);

public record RefreshTokenRequestDto(string RefreshToken);

// ═══════════════════════════════════════════════════════════════════════════════
// Users
// ═══════════════════════════════════════════════════════════════════════════════

public record UserDto(
    string Id,
    string DisplayName,
    string Email,
    string? Title,
    string? Department,
    string? AvatarPath,
    UserRole Role,
    PresenceStatus Presence);

// ═══════════════════════════════════════════════════════════════════════════════
// Conversations
// ═══════════════════════════════════════════════════════════════════════════════

public record ConversationDto(
    string Id,
    ConversationType Type,
    string Name,
    string? Description,
    string? IconPath,
    List<UserDto> Members,
    MessageDto? LastMessage,
    int UnreadCount,
    DateTime LastActivity);

// ═══════════════════════════════════════════════════════════════════════════════
// Messages
// ═══════════════════════════════════════════════════════════════════════════════

public record MessageDto(
    string Id,
    string ConversationId,
    string SenderId,
    string SenderDisplayName,
    string? SenderAvatarUrl,
    string Content,
    MessageType Type,
    string? ReplyToId,
    string? FileId,
    bool IsEdited,
    DateTime SentAt,
    List<ReactionDto> Reactions,
    bool IsDeleted = false,
    bool IsRead = false);

public record ReactionDto(int Emoji, int Count, bool ByCurrentUser);

public record FileAttachmentDto(
    string Id,
    string FileName,
    string ContentType,
    long SizeBytes,
    string Sha256Hash,
    DateTime UploadedAt);

// ═══════════════════════════════════════════════════════════════════════════════
// Announcements
// ═══════════════════════════════════════════════════════════════════════════════

public record AnnouncementDto(
    string Id,
    string Title,
    string Content,
    string AuthorName,
    AnnouncementPriority Priority,
    DateTime PostedAt,
    DateTime? ExpiresAt,
    bool IsAcknowledged);

// ═══════════════════════════════════════════════════════════════════════════════
// Server controller request DTOs (used server-side only)
// ═══════════════════════════════════════════════════════════════════════════════

public record PostAnnouncementDto(
    string Title, string Content,
    AnnouncementPriority Priority = AnnouncementPriority.Normal,
    DateTime? ExpiresAt = null);


public record UpdateProfileDto(
    string? DisplayName, string? Title,
    string? Department, string? AvatarPath);

public record CreateUserDto(
    string Username, string Password,
    string DisplayName, string Email,
    UserRole Role = UserRole.Employee,
    string? Department = null, string? Title = null);

public record CreateDmDto(string OtherUserId);

public record CreateGroupDto(
    string Name, string? Description,
    List<string> MemberIds);

public record ReactDto(int Emoji);


// ═══════════════════════════════════════════════════════════════════════════════
// Admin / Audit
// ═══════════════════════════════════════════════════════════════════════════════

public record AuditLogDto(
    string Id, string UserId, string Action,
    string Details, string? IpAddress, DateTime Timestamp);

public record PagedResultDto<T>(List<T> Items, int TotalCount, bool HasMore);

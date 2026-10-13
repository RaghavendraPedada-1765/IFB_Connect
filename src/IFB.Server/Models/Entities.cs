using IFB.Shared.Enums;

namespace IFB.Server.Models;

// ── Canonical entity names (no Entity suffix) ─────────────────────────────────
// All controllers, repositories, and the hub use these names.

public class User
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Department { get; set; }
    public string? Title { get; set; }
    public string? AvatarPath { get; set; }
    public UserRole Role { get; set; } = UserRole.Employee;
    public PresenceStatus Presence { get; set; } = PresenceStatus.Offline;
    public bool IsActive { get; set; } = true;
    public int TokenVersion { get; set; }
    public bool IsLocalAccount { get; set; } = false;
    public string? PasswordHash { get; set; }
    public string? PasswordSalt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }

    // Nav
    public List<ConversationMember> ConversationMemberships { get; set; } = [];
    public List<RefreshToken> RefreshTokens { get; set; } = [];
}

public class Conversation
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public ConversationType Type { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? IconPath { get; set; }
    public string? CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastActivity { get; set; } = DateTime.UtcNow;
    public int RetentionDays { get; set; } = 90;
    public bool IsArchived { get; set; } = false;

    // Nav
    public List<ConversationMember> Members { get; set; } = [];
    public List<Message> Messages { get; set; } = [];
}

public class ConversationMember
{
    public string ConversationId { get; set; } = string.Empty;
    public Conversation Conversation { get; set; } = null!;
    public string UserId { get; set; } = string.Empty;
    public User User { get; set; } = null!;
    public bool IsAdmin { get; set; } = false;
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
}

public class Message
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ConversationId { get; set; } = string.Empty;
    public Conversation Conversation { get; set; } = null!;
    public string SenderId { get; set; } = string.Empty;
    public User Sender { get; set; } = null!;
    public string Content { get; set; } = string.Empty;
    public MessageType Type { get; set; } = MessageType.Text;
    public string? ReplyToId { get; set; }
    public Message? ReplyTo { get; set; }
    public string? FileId { get; set; }
    public bool IsDeleted { get; set; } = false;
    public bool IsEdited { get; set; } = false;
    public DateTime SentAt { get; set; } = DateTime.UtcNow;
    public DateTime? EditedAt { get; set; }

    // Nav
    public List<MessageReaction> Reactions { get; set; } = [];
    public List<MessageReadReceipt> ReadReceipts { get; set; } = [];
}

public class MessageReaction
{
    public string MessageId { get; set; } = string.Empty;
    public Message Message { get; set; } = null!;
    public string UserId { get; set; } = string.Empty;
    public User User { get; set; } = null!;
    public int Emoji { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class MessageReadReceipt
{
    public string MessageId { get; set; } = string.Empty;
    public Message Message { get; set; } = null!;
    public string UserId { get; set; } = string.Empty;
    public User User { get; set; } = null!;
    public DateTime ReadAt { get; set; } = DateTime.UtcNow;
}

public class FileAttachment
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string OriginalFileName { get; set; } = string.Empty;
    public string? ContentType { get; set; }
    public long SizeBytes { get; set; }
    public string StoragePath { get; set; } = string.Empty;
    public string Sha256Hash { get; set; } = string.Empty;
    public string UploadedByUserId { get; set; } = string.Empty;
    public User UploadedBy { get; set; } = null!;
    public string? ConversationId { get; set; }
    public bool IsAvScanPassed { get; set; } = false;
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
}

public class Announcement
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string AuthorId { get; set; } = string.Empty;
    public User? Author { get; set; }
    public AnnouncementPriority Priority { get; set; } = AnnouncementPriority.Normal;
    public DateTime PostedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresAt { get; set; }

    // Nav
    public List<AnnouncementAck> Acknowledgements { get; set; } = [];
}

public class AnnouncementAck
{
    public string AnnouncementId { get; set; } = string.Empty;
    public Announcement Announcement { get; set; } = null!;
    public string UserId { get; set; } = string.Empty;
    public User? User { get; set; }
    public DateTime AcknowledgedAt { get; set; } = DateTime.UtcNow;
}

public class RefreshToken
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Token { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public User User { get; set; } = null!;
    public DateTime ExpiresAt { get; set; }
    public bool IsRevoked { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? ReplacedByToken { get; set; }
}

public class AuditLog
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string UserId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    public string? IpAddress { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public class OfflineMessage
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string RecipientUserId { get; set; } = string.Empty;
    public string MessageId { get; set; } = string.Empty;
    public string ConversationId { get; set; } = string.Empty;
    public DateTime QueuedAt { get; set; } = DateTime.UtcNow;
    public bool IsDelivered { get; set; } = false;
}

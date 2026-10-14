using IFB.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace IFB.Server.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }
    protected AppDbContext(DbContextOptions options) : base(options) { }

    // ── DbSets ────────────────────────────────────────────────────────────────
    public DbSet<User> Users => Set<User>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<ConversationMember> ConversationMembers => Set<ConversationMember>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<MessageReaction> MessageReactions => Set<MessageReaction>();
    public DbSet<MessageReadReceipt> MessageReadReceipts => Set<MessageReadReceipt>();
    public DbSet<FileAttachment> FileAttachments => Set<FileAttachment>();
    public DbSet<Announcement> Announcements => Set<Announcement>();
    public DbSet<AnnouncementAck> AnnouncementAcks => Set<AnnouncementAck>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<OfflineMessage> OfflineMessages => Set<OfflineMessage>();

    protected override void OnModelCreating(ModelBuilder m)
    {
        // ── User ──────────────────────────────────────────────────────────────
        m.Entity<User>(e =>
        {
            e.HasKey(u => u.Id);
            e.HasIndex(u => u.Username).IsUnique();
            e.HasIndex(u => u.Email).IsUnique();
            e.Property(u => u.Username).HasMaxLength(64).IsRequired();
            e.Property(u => u.DisplayName).HasMaxLength(128).IsRequired();
            e.Property(u => u.Email).HasMaxLength(256).IsRequired();
        });

        // ── Conversation ──────────────────────────────────────────────────────
        m.Entity<Conversation>(e =>
        {
            e.HasKey(c => c.Id);
            e.HasIndex(c => c.LastActivity);
            e.Property(c => c.Name).HasMaxLength(128).IsRequired();
        });

        // ── ConversationMember (composite PK) ─────────────────────────────────
        m.Entity<ConversationMember>(e =>
        {
            e.HasKey(cm => new { cm.ConversationId, cm.UserId });
            e.HasOne(cm => cm.Conversation)
             .WithMany(c => c.Members)
             .HasForeignKey(cm => cm.ConversationId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(cm => cm.User)
             .WithMany(u => u.ConversationMemberships)
             .HasForeignKey(cm => cm.UserId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        // ── Message ───────────────────────────────────────────────────────────
        m.Entity<Message>(e =>
        {
            e.HasKey(msg => msg.Id);
            e.HasIndex(msg => new { msg.ConversationId, msg.SentAt });
            e.HasIndex(msg => msg.SenderId);
            e.HasOne(msg => msg.Conversation)
             .WithMany(c => c.Messages)
             .HasForeignKey(msg => msg.ConversationId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(msg => msg.Sender)
             .WithMany()
             .HasForeignKey(msg => msg.SenderId)
             .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(msg => msg.ReplyTo)
             .WithMany()
             .HasForeignKey(msg => msg.ReplyToId)
             .OnDelete(Database.IsSqlServer() ? DeleteBehavior.NoAction : DeleteBehavior.SetNull)
             .IsRequired(false);
            e.Property(msg => msg.Content).HasMaxLength(10_000).IsRequired();
        });

        // ── MessageReaction (composite PK: MessageId + UserId + Emoji) ────────
        m.Entity<MessageReaction>(e =>
        {
            e.HasKey(r => new { r.MessageId, r.UserId, r.Emoji });
            e.HasOne(r => r.Message)
             .WithMany(msg => msg.Reactions)
             .HasForeignKey(r => r.MessageId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(r => r.User)
             .WithMany()
             .HasForeignKey(r => r.UserId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        // ── MessageReadReceipt (composite PK: MessageId + UserId) ─────────────
        m.Entity<MessageReadReceipt>(e =>
        {
            e.HasKey(r => new { r.MessageId, r.UserId });
            e.HasOne(r => r.Message)
             .WithMany(msg => msg.ReadReceipts)
             .HasForeignKey(r => r.MessageId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(r => r.User)
             .WithMany()
             .HasForeignKey(r => r.UserId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        // ── FileAttachment ────────────────────────────────────────────────────
        m.Entity<FileAttachment>(e =>
        {
            e.HasKey(f => f.Id);
            e.HasOne(f => f.UploadedBy)
             .WithMany()
             .HasForeignKey(f => f.UploadedByUserId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        // ── Announcement ──────────────────────────────────────────────────────
        m.Entity<Announcement>(e =>
        {
            e.HasKey(a => a.Id);
            e.HasOne(a => a.Author)
             .WithMany()
             .HasForeignKey(a => a.AuthorId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        // ── AnnouncementAck (composite PK) ────────────────────────────────────
        m.Entity<AnnouncementAck>(e =>
        {
            e.HasKey(a => new { a.AnnouncementId, a.UserId });
            e.HasOne(a => a.Announcement)
             .WithMany(ann => ann.Acknowledgements)
             .HasForeignKey(a => a.AnnouncementId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(a => a.User)
             .WithMany()
             .HasForeignKey(a => a.UserId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        // ── RefreshToken ──────────────────────────────────────────────────────
        m.Entity<RefreshToken>(e =>
        {
            e.HasKey(t => t.Id);
            e.HasIndex(t => t.Token).IsUnique();
            e.HasOne(t => t.User)
             .WithMany(u => u.RefreshTokens)
             .HasForeignKey(t => t.UserId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // ── AuditLog ──────────────────────────────────────────────────────────
        m.Entity<AuditLog>(e =>
        {
            e.HasKey(l => l.Id);
            e.HasIndex(l => l.UserId);
            e.HasIndex(l => l.Timestamp);
        });

        // ── OfflineMessage ────────────────────────────────────────────────────
        m.Entity<OfflineMessage>(e =>
        {
            e.HasKey(o => o.Id);
            e.HasIndex(o => new { o.RecipientUserId, o.IsDelivered });
        });
    }
}

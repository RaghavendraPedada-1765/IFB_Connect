using IFB.Server.Auth;
using IFB.Server.Models;
using IFB.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace IFB.Server.Data;

public static class DatabaseBootstrap
{
    public static async Task InitializeAsync(AppDbContext db, IConfiguration config)
    {
        await db.Users.ExecuteUpdateAsync(u => u.SetProperty(x => x.Presence, PresenceStatus.Offline));
        if (!await db.Users.AnyAsync())
        {
        var username = config["Bootstrap:AdminUsername"];
        var password = config["Bootstrap:AdminPassword"];
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException("For the first start set Bootstrap__AdminUsername and Bootstrap__AdminPassword (at least 12 characters).");
        if (password.Length < 12) throw new InvalidOperationException("Bootstrap administrator password must have at least 12 characters.");
        var (hash, salt) = AuthService.HashPassword(password);
        db.Users.Add(new User { Username = username.Trim().ToLowerInvariant(),
            DisplayName = "Administrator", Email = config["Bootstrap:AdminEmail"] ?? "admin@ifb.local",
            Role = UserRole.Admin, IsLocalAccount = true, PasswordHash = hash, PasswordSalt = salt });
        await db.SaveChangesAsync();
        }
        var admin = await db.Users.FirstOrDefaultAsync(u => u.Role == UserRole.Admin && u.IsActive);
        if (admin == null) return;
        foreach (var name in new[] { "Production", "Quality", "Maintenance", "HR", "IT" })
        {
            if (await db.Conversations.AnyAsync(c => c.Type == ConversationType.Channel && c.Name == name)) continue;
            var channel = new Conversation { Name = name, Type = ConversationType.Channel, CreatedByUserId = admin.Id,
                RetentionDays = config.GetValue("Retention:DefaultDays", 90) };
            channel.Members.Add(new ConversationMember { UserId = admin.Id, IsAdmin = true }); db.Conversations.Add(channel);
        }
        await db.SaveChangesAsync();
    }
}

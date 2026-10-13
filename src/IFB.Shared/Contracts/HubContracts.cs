using IFB.Shared.DTOs;
using IFB.Shared.Enums;

namespace IFB.Shared.Contracts;

// ── DTOs used exclusively by hub methods ──────────────────────────────────────

public record SendMessageDto(
    string ConversationId,
    string? Content,
    MessageType Type,
    string? ReplyToId,
    string? FileId);

public record EditMessageDto(string MessageId, string NewContent);
public record DeleteMessageDto(string MessageId);
public record ToggleReactionDto(string MessageId, int Emoji);
public record MarkReadDto(string MessageId);
public record TypingDto(string ConversationId, bool IsTyping);
public record UpdatePresenceDto(PresenceStatus Status);

// ── Hub event DTOs (server → client) ─────────────────────────────────────────

public record MessageEditedDto(
    string MessageId, string ConversationId,
    string NewContent, DateTime EditedAt);

public record MessageDeletedDto(string MessageId, string ConversationId);

public record ReactionUpdatedDto(
    string MessageId, string ConversationId,
    string UserId, int Emoji, bool Added);

public record ReadReceiptDto(
    string MessageId, string ConversationId,
    string ReadByUserId, DateTime ReadAt);

public record TypingEventDto(
    string ConversationId, string UserId,
    string DisplayName, bool IsTyping);

public record PresenceUpdateDto(
    string UserId, PresenceStatus Status, DateTime UpdatedAt);

// ── IServerHub — methods clients can invoke ───────────────────────────────────
/// <summary>Methods the server Hub exposes — called by clients via SignalR.</summary>
public interface IServerHub
{
    Task SendMessageAsync(SendMessageDto dto);
    Task EditMessageAsync(EditMessageDto dto);
    Task DeleteMessageAsync(DeleteMessageDto dto);
    Task ToggleReactionAsync(ToggleReactionDto dto);
    Task MarkReadAsync(MarkReadDto dto);
    Task SendTypingAsync(TypingDto dto);
    Task UpdatePresenceAsync(UpdatePresenceDto dto);
    Task JoinConversationAsync(string conversationId);
    Task LeaveConversationAsync(string conversationId);
}

// ── IClientHub — methods the server calls on clients ─────────────────────────
/// <summary>Methods the client implements — invoked by the server Hub.</summary>
public interface IClientHub
{
    Task MessageReceivedAsync(MessageDto message);
    Task MessageEditedAsync(MessageEditedDto dto);
    Task MessageDeletedAsync(MessageDeletedDto dto);
    Task ReactionUpdatedAsync(ReactionUpdatedDto dto);
    Task ReadReceiptAsync(ReadReceiptDto dto);
    Task TypingAsync(TypingEventDto dto);
    Task PresenceChangedAsync(PresenceUpdateDto dto);
    Task ConversationUpdatedAsync(ConversationDto dto);
    Task AnnouncementPostedAsync(AnnouncementDto dto);
    Task ErrorAsync(string message);
    Task UserForceLogoutAsync(string reason);
}

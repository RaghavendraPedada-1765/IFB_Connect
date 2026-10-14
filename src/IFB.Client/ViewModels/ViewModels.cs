using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IFB.Client.Services;
using IFB.Shared.Contracts;
using IFB.Shared.DTOs;
using IFB.Shared.Enums;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.ObjectModel;

namespace IFB.Client.ViewModels;

// ═══════════════════════════════════════════════════════════════════════════════
// Conversation item (sidebar list)
// ═══════════════════════════════════════════════════════════════════════════════

public sealed partial class ConversationItemViewModel : ObservableObject
{
    public required string Id { get; init; }
    public required ConversationType Type { get; init; }
    public required string Name { get; init; }
    public required List<UserDto> Members { get; init; }

    [ObservableProperty] private string? _lastMessagePreview;
    [ObservableProperty] private DateTime _lastActivity;
    [ObservableProperty] private int _unreadCount;
    [ObservableProperty] private bool _hasUnread;

    // For DMs: resolve the other person's presence
    public UserDto? OtherMember(string myUserId) =>
        Type == ConversationType.DirectMessage
            ? Members.FirstOrDefault(m => m.Id != myUserId)
            : null;
}

// ═══════════════════════════════════════════════════════════════════════════════
// Message item (chat list)
// ═══════════════════════════════════════════════════════════════════════════════

public sealed partial class MessageItemViewModel : ObservableObject
{
    public string? FileId { get; init; }
    public string? ReplyToId { get; init; }
    public bool HasFile => FileId != null;
    public bool HasImageAttachment => HasFile && !IsDeleted && new[] { ".png", ".jpg", ".jpeg", ".gif", ".bmp" }.Contains(System.IO.Path.GetExtension(Content).ToLowerInvariant());
    public void RefreshAppearance() => OnPropertyChanged(nameof(IsFromCurrentUser));
    [ObservableProperty] private string _readStatus = "";
    [ObservableProperty] private string? _replyPreview;
    public string ReactionSummary => string.Join("  ", Reactions.Select(r => $"{(ReactionEmoji)r.Emoji} {r.Count}"));
    partial void OnReactionsChanged(List<ReactionDto> value) => OnPropertyChanged(nameof(ReactionSummary));
    public required string Id { get; init; }
    public required string ConversationId { get; init; }
    public required string SenderId { get; init; }
    public required string SenderDisplayName { get; init; }
    public string? SenderAvatarUrl { get; init; }

    [ObservableProperty] private string _content = string.Empty;
    [ObservableProperty] private bool _isEdited;
    [ObservableProperty] private bool _isDeleted;
    partial void OnIsDeletedChanged(bool value) => OnPropertyChanged(nameof(HasImageAttachment));
    partial void OnContentChanged(string value) => OnPropertyChanged(nameof(HasImageAttachment));
    [ObservableProperty] private DateTime _sentAt;
    public string Timestamp => SentAt.ToLocalTime().ToString("g");
    [ObservableProperty] private List<ReactionDto> _reactions = [];
    [ObservableProperty] private bool _isFromCurrentUser;

    // Grouping — true when same sender within 2 min of previous
    public bool IsGrouped { get; set; }

    public static MessageItemViewModel FromDto(MessageDto dto, string currentUserId) => new()
    {
        Id = dto.Id,
        ConversationId = dto.ConversationId,
        SenderId = dto.SenderId,
        SenderDisplayName = dto.SenderDisplayName,
        SenderAvatarUrl = dto.SenderAvatarUrl,
        Content = dto.Content,
        FileId = dto.FileId, ReplyToId = dto.ReplyToId, IsDeleted = dto.IsDeleted,
        ReadStatus = dto.IsRead ? "Read" : "",
        IsEdited = dto.IsEdited,
        SentAt = dto.SentAt,
        Reactions = dto.Reactions,
        IsFromCurrentUser = dto.SenderId == currentUserId
    };
}

// ═══════════════════════════════════════════════════════════════════════════════
// Login ViewModel — wired to ClientAuthService
// ═══════════════════════════════════════════════════════════════════════════════

public sealed partial class LoginViewModel : ObservableObject
{
    private readonly IClientAuthService _auth;
    private readonly IDiscoveryService _discovery;
    private readonly ServerEndpoint _endpoint;
    private readonly INavigationService _nav;

    [ObservableProperty] private string _username = string.Empty;
    [ObservableProperty] private string _password = string.Empty;
    [ObservableProperty] private string _serverAddress = string.Empty;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private bool _isDiscovering;
    [ObservableProperty] private string _discoveryStatus = "Searching for IFB Connect server…";
    [ObservableProperty] private bool _serverFound;

    public LoginViewModel(
        IClientAuthService auth,
        IDiscoveryService discovery,
        INavigationService nav, ServerEndpoint endpoint)
    {
        _auth = auth;
        _discovery = discovery;
        _endpoint = endpoint;
        ServerAddress = endpoint.Address.ToString();
        _nav = nav;
    }

    [RelayCommand]
    private async Task DiscoverServerAsync()
    {
        IsDiscovering = true;
        DiscoveryStatus = "Searching for IFB Connect server…";
        ServerFound = false;

        try
        {
            var result = await _discovery.DiscoverAsync(timeoutMs: 4000);
            if (result != null)
            {
                ServerAddress = result.ServerUrl;
                ServerFound = true;
                DiscoveryStatus = $"Found: {result.ServerUrl}";
            }
            else
            {
                DiscoveryStatus = "Auto-discovery failed — enter server address manually.";
            }
        }
        catch
        {
            DiscoveryStatus = "Discovery error — enter server address manually.";
        }
        finally
        {
            IsDiscovering = false;
        }
    }

    [RelayCommand]
    private async Task LoginAsync()
    {
        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
        {
            ErrorMessage = "Username and password are required.";
            return;
        }

        IsLoading = true;
        ErrorMessage = null;

        try
        {
            _endpoint.SetAddress(ServerAddress);
            var success = await _auth.LoginAsync(Username.Trim(), Password);
            if (success)
            {
                Password = string.Empty; // Clear sensitive data
                _nav.NavigateTo("Main");
            }
            else
            {
                ErrorMessage = "Invalid credentials. Please check your username and password.";
            }
        }
        catch (HttpRequestException)
        {
            ErrorMessage = "Cannot reach the server. Verify you are on the company network.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Login error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
// Main Shell ViewModel
// ═══════════════════════════════════════════════════════════════════════════════

public sealed partial class MainViewModel : ObservableObject
{
    private readonly IClientAuthService _auth;
    private readonly IHubConnectionService _hub;
    private readonly INavigationService _nav;
    private readonly IApiService _api;

    [ObservableProperty] private UserDto? _currentUser;
    [ObservableProperty] private ObservableCollection<ConversationItemViewModel> _conversations = [];
    [ObservableProperty] private ConversationItemViewModel? _selectedConversation;
    [ObservableProperty] private ConnectionState _connectionState = ConnectionState.Disconnected;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _announcementText;
    [ObservableProperty] private AnnouncementPriority _announcementPriority;
    [ObservableProperty] private bool _hasAnnouncement;

    public MainViewModel(
        IClientAuthService auth,
        IHubConnectionService hub,
        INavigationService nav,
        IApiService api)
    {
        _auth = auth;
        _hub = hub;
        _nav = nav;
        _api = api;

        _hub.ConnectionStateChanged += (_, state) => ConnectionState = state;

        // Hub events → update conversations list
        _hub.ConversationUpdated += OnConversationUpdated;
        _hub.MessageReceived += OnMessageReceived;
    }

    [RelayCommand]
    private async Task InitializeAsync()
    {
        IsLoading = true;
        try
        {
            CurrentUser = _auth.CurrentUser;
            await _hub.ConnectAsync();
            await LoadConversationsAsync();
            await LoadAnnouncementsAsync();
        }
        finally
        {
            IsLoading = false;
        }
    }

    public Task RefreshConversationsAsync() => LoadConversationsAsync();

    private async Task LoadConversationsAsync()
    {
        try
        {
            var convs = await _api.GetConversationsAsync();
            Conversations.Clear();
            foreach (var c in convs)
            {
                Conversations.Add(new ConversationItemViewModel
                {
                    Id = c.Id,
                    Type = c.Type,
                    Name = c.Name,
                    Members = c.Members,
                    LastMessagePreview = c.LastMessage?.Content,
                    LastActivity = c.LastActivity,
                    UnreadCount = c.UnreadCount,
                    HasUnread = c.UnreadCount > 0
                });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"LoadConversations failed: {ex.Message}");
            throw;
        }
    }

    private async Task LoadAnnouncementsAsync()
    {
        try
        {
            var announcements = await _api.GetAnnouncementsAsync();
            var unacked = announcements.FirstOrDefault(a => !a.IsAcknowledged);
            if (unacked != null)
            {
                AnnouncementText = $"{unacked.Title}: {unacked.Content}";
                AnnouncementPriority = unacked.Priority;
                HasAnnouncement = true;
            }
        }
        catch { /* Announcements are optional — don't crash on failure */ }
    }

    [RelayCommand]
    private async Task SelectConversationAsync(ConversationItemViewModel conv)
    {
        SelectedConversation = conv;
        conv.UnreadCount = 0;
        conv.HasUnread = false;

        // Tell hub to join the group for this conversation
        await _hub.JoinConversationAsync(conv.Id);
    }

    [RelayCommand]
    private async Task LogoutAsync()
    {
        await _auth.LogoutAsync();
        await _hub.DisconnectAsync();
        _nav.NavigateTo("Login");
    }

    private void OnMessageReceived(object? sender, MessageDto msg)
    {
        var conv = Conversations.FirstOrDefault(c => c.Id == msg.ConversationId);
        if (conv != null)
        {
            conv.LastMessagePreview = msg.Content;
            conv.LastActivity = msg.SentAt;
            if (msg.SenderId != _auth.CurrentUser?.Id && App.Current.MainWindow?.IsConversationVisible(conv.Id) != true)
            {
                conv.UnreadCount++;
                conv.HasUnread = true;
            }
        }
    }

    private void OnConversationUpdated(object? sender, ConversationDto dto)
    {
        var existing = Conversations.FirstOrDefault(c => c.Id == dto.Id);
        if (existing == null)
        {
            Conversations.Insert(0, new ConversationItemViewModel
            {
                Id = dto.Id, Type = dto.Type, Name = dto.Name,
                Members = dto.Members, LastActivity = dto.LastActivity
            });
        }
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
// Chat ViewModel — real SignalR + REST
// ═══════════════════════════════════════════════════════════════════════════════

public sealed partial class ChatViewModel : ObservableObject
{
    private readonly IHubConnectionService _hub;
    private readonly IApiService _api;
    private readonly IClientAuthService _auth;

    private string? _conversationId;
    private readonly HashSet<string> _readMessageIds = [];
    private CancellationTokenSource? _typingCts;

    [ObservableProperty] private ObservableCollection<MessageItemViewModel> _messages = [];
    [ObservableProperty] private string _composerText = string.Empty;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _hasMoreHistory;
    [ObservableProperty] private string? _typingIndicatorText;
    [ObservableProperty] private bool _isTypingVisible;
    [ObservableProperty] private MessageItemViewModel? _replyTarget;

    // Tracks who is typing: userId → display name
    private readonly Dictionary<string, string> _typingUsers = [];

    public ChatViewModel(IHubConnectionService hub, IApiService api, IClientAuthService auth)
    {
        _hub = hub;
        _api = api;
        _auth = auth;

        // Hub event subscriptions
        _hub.MessageReceived += OnMessageReceived;
        _hub.MessageEdited += OnMessageEdited;
        _hub.MessageDeleted += OnMessageDeleted;
        _hub.ReactionUpdated += OnReactionUpdated;
        _hub.TypingReceived += OnTypingReceived;
        _hub.ReadReceiptReceived += OnReadReceipt;
    }

    /// <summary>Called when switching to a conversation.</summary>
    public async Task LoadConversationAsync(string conversationId)
    {
        _conversationId = conversationId;
        _typingUsers.Clear(); UpdateTypingIndicator(); ReplyTarget = null;
        Messages.Clear();
        IsLoading = true;

        try
        {
            var page = await _api.GetMessageHistoryAsync(conversationId, before: null, limit: 50);
            if (_conversationId != conversationId) return;
            foreach (var dto in page.Messages)
                if (!Messages.Any(m => m.Id == dto.Id)) Messages.Add(MessageItemViewModel.FromDto(dto, _auth.CurrentUser?.Id ?? ""));

            HasMoreHistory = page.HasMore;
            ApplyGrouping();
            await MarkVisibleMessagesReadAsync();
        }
        finally
        {
            if (_conversationId == conversationId) IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task SendMessageAsync()
    {
        if (string.IsNullOrWhiteSpace(ComposerText) || _conversationId == null) return;

        var text = ComposerText.Trim();
        await _hub.SendMessageAsync(new SendMessageDto(
            _conversationId, text,
            MessageType.Text,
            ReplyTarget?.Id,
            null));
        ComposerText = string.Empty;
        ReplyTarget = null;
    }

    [RelayCommand]
    private async Task LoadMoreHistoryAsync()
    {
        if (_conversationId == null || !HasMoreHistory || IsLoading) return;

        var oldest = Messages.FirstOrDefault();
        if (oldest == null) return;

        IsLoading = true;
        try
        {
            var page = await _api.GetMessageHistoryAsync(
                _conversationId, before: oldest.SentAt, limit: 50, beforeId: oldest.Id);

            // Prepend older messages
            var index = 0;
            foreach (var dto in page.Messages)
                Messages.Insert(index++, MessageItemViewModel.FromDto(dto, _auth.CurrentUser?.Id ?? ""));

            HasMoreHistory = page.HasMore;
            ApplyGrouping();
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void SetReplyTarget(MessageItemViewModel? message) => ReplyTarget = message;

    [RelayCommand]
    private void ClearReply() => ReplyTarget = null;

    [RelayCommand]
    private async Task ReactAsync(object? parameter)
    {
        if (parameter is not (string msgId, int emoji)) return;
        await _hub.ToggleReactionAsync(new ToggleReactionDto(msgId, emoji));
    }

    private DateTime _lastTypingSentUtc = DateTime.MinValue;
    private string? _lastTypingConversationId;

    public async Task NotifyTypingAsync()
    {
        if (_conversationId == null) return;

        // Send "is typing" at most every 2.5 seconds instead of on every keystroke,
        // and auto-cancel 3 seconds after the last keystroke.
        var now = DateTime.UtcNow;
        if (_lastTypingConversationId != _conversationId || now - _lastTypingSentUtc > TimeSpan.FromSeconds(2.5))
        {
            try { await _hub.SendTypingAsync(new TypingDto(_conversationId, true)); } catch { return; }
            _lastTypingSentUtc = now; _lastTypingConversationId = _conversationId;
        }

        _typingCts?.Cancel();
        _typingCts = new CancellationTokenSource();
        var token = _typingCts.Token;

        _ = Task.Delay(3000, token).ContinueWith(async _ =>
        {
            if (!token.IsCancellationRequested)
            {
                _lastTypingSentUtc = DateTime.MinValue; // the next keystroke announces typing again
                try { await _hub.SendTypingAsync(new TypingDto(_conversationId!, false)); } catch { }
            }
        }, TaskScheduler.Current);
    }

    // ── Hub event handlers ────────────────────────────────────────────────────

    private void OnMessageReceived(object? sender, MessageDto dto)
    {
        if (dto.ConversationId != _conversationId) return;
        if (Messages.Any(m => m.Id == dto.Id)) return;
        var vm = MessageItemViewModel.FromDto(dto, _auth.CurrentUser?.Id ?? "");
        Messages.Add(vm);
        ApplyGrouping();

        if (dto.SenderId != _auth.CurrentUser?.Id && App.Current.MainWindow?.IsConversationVisible(dto.ConversationId) == true)
            _ = MarkVisibleMessagesReadAsync();
    }

    public async Task MarkVisibleMessagesReadAsync()
    {
        if (_conversationId == null || App.Current.MainWindow?.IsConversationVisible(_conversationId) != true) return;
        try
        {
            // The server marks every earlier incoming message read with this one call, so only
            // the newest incoming message needs to be sent.
            var newest = Messages.LastOrDefault(m => !m.IsFromCurrentUser);
            if (newest != null && _readMessageIds.Add(newest.Id))
            {
                try { await _hub.MarkReadAsync(new MarkReadDto(newest.Id)); }
                catch { _readMessageIds.Remove(newest.Id); throw; }
            }
            var main = App.Services.GetRequiredService<MainViewModel>();
            if (main.SelectedConversation?.Id == _conversationId)
            {
                main.SelectedConversation.UnreadCount = 0;
                main.SelectedConversation.HasUnread = false;
            }
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine("Read receipt pending: " + ex.Message); }
    }

    /// <summary>A receipt means "read up to and including this message".</summary>
    private void OnReadReceipt(object? sender, ReadReceiptDto receipt)
    {
        if (receipt.ConversationId != _conversationId || receipt.ReadByUserId == _auth.CurrentUser?.Id) return;
        var target = Messages.FirstOrDefault(m => m.Id == receipt.MessageId);
        if (target == null) return;
        foreach (var message in Messages)
        {
            if (message.IsFromCurrentUser) message.ReadStatus = "Read";
            if (ReferenceEquals(message, target)) break;
        }
    }

    private void OnMessageEdited(object? sender, MessageEditedDto dto)
    {
        if (dto.ConversationId != _conversationId) return;
        var msg = Messages.FirstOrDefault(m => m.Id == dto.MessageId);
        if (msg != null) { msg.Content = dto.NewContent; msg.IsEdited = true; }
    }

    private void OnMessageDeleted(object? sender, MessageDeletedDto dto)
    {
        if (dto.ConversationId != _conversationId) return;
        var msg = Messages.FirstOrDefault(m => m.Id == dto.MessageId);
        if (msg != null) { msg.Content = "[This message was deleted]"; msg.IsDeleted = true; }
    }

    private void OnReactionUpdated(object? sender, ReactionUpdatedDto dto)
    {
        if (dto.ConversationId != _conversationId) return;
        var msg = Messages.FirstOrDefault(m => m.Id == dto.MessageId);
        if (msg == null) return;

        var reactions = msg.Reactions.ToList();
        var existing = reactions.FirstOrDefault(r => r.Emoji == dto.Emoji);

        if (dto.Added)
        {
            if (existing != null)
                reactions[reactions.IndexOf(existing)] = new ReactionDto(
                    existing.Emoji, existing.Count + 1,
                    dto.UserId == _auth.CurrentUser?.Id || existing.ByCurrentUser);
            else
                reactions.Add(new ReactionDto(dto.Emoji, 1, dto.UserId == _auth.CurrentUser?.Id));
        }
        else
        {
            if (existing != null && existing.Count <= 1)
                reactions.Remove(existing);
            else if (existing != null)
                reactions[reactions.IndexOf(existing)] = new ReactionDto(
                    existing.Emoji, existing.Count - 1,
                    dto.UserId != _auth.CurrentUser?.Id && existing.ByCurrentUser);
        }
        msg.Reactions = reactions;
    }

    private void OnTypingReceived(object? sender, TypingEventDto dto)
    {
        if (dto.ConversationId != _conversationId) return;
        if (dto.UserId == _auth.CurrentUser?.Id) return;

        if (dto.IsTyping)
            _typingUsers[dto.UserId] = dto.DisplayName;
        else
            _typingUsers.Remove(dto.UserId);

        UpdateTypingIndicator();
    }

    private void UpdateTypingIndicator()
    {
        var names = _typingUsers.Values.ToList();
        TypingIndicatorText = names.Count switch
        {
            0 => null,
            1 => $"{names[0]} is typing…",
            2 => $"{names[0]} and {names[1]} are typing…",
            _ => "Several people are typing…"
        };
        IsTypingVisible = names.Count > 0;
    }

    private void ApplyGrouping()
    {
        foreach (var message in Messages)
            message.ReplyPreview = message.ReplyToId == null ? null : "↪ " + (Messages.FirstOrDefault(m => m.Id == message.ReplyToId)?.Content ?? "Earlier message");
        for (int i = 0; i < Messages.Count; i++)
        {
            if (i == 0) { Messages[i].IsGrouped = false; continue; }
            var prev = Messages[i - 1];
            Messages[i].IsGrouped =
                prev.SenderId == Messages[i].SenderId &&
                (Messages[i].SentAt - prev.SentAt).TotalMinutes < 2;
        }
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
// Directory ViewModel
// ═══════════════════════════════════════════════════════════════════════════════

public sealed partial class DirectoryViewModel : ObservableObject
{
    private readonly IApiService _api;
    private readonly INavigationService _nav;

    [ObservableProperty] private ObservableCollection<UserDto> _users = [];
    [ObservableProperty] private string _searchQuery = string.Empty;
    [ObservableProperty] private string? _selectedDepartment;
    [ObservableProperty] private ObservableCollection<string> _departments = [];
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private int _totalCount;

    public DirectoryViewModel(IApiService api, INavigationService nav)
    {
        _api = api;
        _nav = nav;
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            var depts = await _api.GetDepartmentsAsync();
            Departments.Clear();
            foreach (var d in depts) Departments.Add(d);

            await SearchAsync();
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        IsLoading = true;
        try
        {
            var result = await _api.SearchUsersAsync(SearchQuery, SelectedDepartment);
            TotalCount = result.Total;
            Users.Clear();
            foreach (var u in result.Users) Users.Add(u);
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private void StartDm(UserDto user) =>
        _nav.NavigateTo("Chat", new { UserId = user.Id });
}

// ═══════════════════════════════════════════════════════════════════════════════
// Settings ViewModel
// ═══════════════════════════════════════════════════════════════════════════════

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly IThemeService _theme;
    private readonly IClientAuthService _auth;
    private readonly IHubConnectionService _hub;
    private readonly INavigationService _nav;

    [ObservableProperty] private string _selectedTheme = "Auto";
    [ObservableProperty] private bool _enableNotifications = true;
    [ObservableProperty] private bool _enableSounds = true;
    [ObservableProperty] private bool _performanceMode;
    [ObservableProperty] private PresenceStatus _myPresence = PresenceStatus.Online;

    public SettingsViewModel(
        IThemeService theme, IClientAuthService auth,
        IHubConnectionService hub, INavigationService nav)
    {
        _theme = theme;
        _auth = auth;
        _hub = hub;
        _nav = nav;
    }

    [RelayCommand]
    private async Task SetPresenceAsync(PresenceStatus status)
    {
        MyPresence = status;
        await _hub.UpdatePresenceAsync(new UpdatePresenceDto(status));
    }

    [RelayCommand]
    private void ChangeTheme(string theme)
    {
        SelectedTheme = theme;
        _theme.SetTheme(theme);
    }

    [RelayCommand]
    private void TogglePerformanceMode()
    {
        PerformanceMode = !PerformanceMode;
        _theme.SetPerformanceMode(PerformanceMode);
    }
}

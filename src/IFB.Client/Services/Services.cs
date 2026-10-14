using IFB.Shared.Contracts;
using IFB.Shared.DTOs;
using IFB.Shared.Enums;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace IFB.Client.Services;

// ═══════════════════════════════════════════════════════════════════════════════
// Connection state enum
// ═══════════════════════════════════════════════════════════════════════════════

public enum ConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Reconnecting
}

// ═══════════════════════════════════════════════════════════════════════════════
// Auth Service
// ═══════════════════════════════════════════════════════════════════════════════

public interface IClientAuthService
{
    bool IsAuthenticated { get; }
    UserDto? CurrentUser { get; }
    string? AccessToken { get; }
    Task<bool> LoginAsync(string username, string password);
    Task<bool> WindowsLoginAsync();
    Task LogoutAsync();
    Task<bool> TryRefreshAsync(bool force = false);
    event EventHandler<bool> AuthStateChanged;
}

public sealed class ClientAuthService : IClientAuthService
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _config;
    private readonly ServerEndpoint _endpoint;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    private string? _accessToken;
    private string? _refreshToken;
    private DateTime _tokenExpiry;

    public bool IsAuthenticated => _accessToken != null && _tokenExpiry > DateTime.UtcNow.AddMinutes(1);
    public UserDto? CurrentUser { get; private set; }
    public string? AccessToken => _accessToken;
    public event EventHandler<bool>? AuthStateChanged;

    public ClientAuthService(IHttpClientFactory httpFactory, IConfiguration config, ServerEndpoint endpoint)
    {
        _httpFactory = httpFactory;
        _config = config;
        _endpoint = endpoint;
    }

    public async Task<bool> LoginAsync(string username, string password)
    {
        var client = _httpFactory.CreateClient("IFBAuth");
        try
        {
            var resp = await client.PostAsJsonAsync("api/v1/auth/login",
                new LoginRequestDto(username, password));
            if (!resp.IsSuccessStatusCode) return false;

            var result = await resp.Content.ReadFromJsonAsync<LoginResponseDto>();
            if (result == null) return false;

            _accessToken = result.AccessToken;
            _refreshToken = result.RefreshToken;
            _tokenExpiry = result.ExpiresAt;
            CurrentUser = result.User;

            SaveRefreshToken(_refreshToken);
            AuthStateChanged?.Invoke(this, true);
            return true;
        }
        catch (HttpRequestException) { throw; }
    }

    public async Task LogoutAsync()
    {
        // The server revokes by refresh token alone, so this works even after the access token expired.
        var refreshToken = _refreshToken ?? LoadRefreshToken();
        if (refreshToken != null)
        {
            try
            {
                using var client = _httpFactory.CreateClient("IFBAuth");
                using var response = await client.PostAsJsonAsync("api/v1/auth/logout",
                    new RefreshTokenRequestDto(refreshToken));
                if (!response.IsSuccessStatusCode)
                    System.Diagnostics.Debug.WriteLine($"Server sign-out returned {(int)response.StatusCode}");
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("Server sign-out failed: " + ex.Message); }
        }
        _accessToken = null;
        _refreshToken = null;
        CurrentUser = null;
        DeleteRefreshToken();
        AuthStateChanged?.Invoke(this, false);
    }

    public async Task<bool> WindowsLoginAsync()
    {
        using var client = _httpFactory.CreateClient("IFBAuth");
        using var response = await client.PostAsync("api/v1/auth/windows", null);
        if (!response.IsSuccessStatusCode) return false;
        var result = await response.Content.ReadFromJsonAsync<LoginResponseDto>(); if (result == null) return false;
        _accessToken = result.AccessToken; _refreshToken = result.RefreshToken; _tokenExpiry = result.ExpiresAt; CurrentUser = result.User;
        SaveRefreshToken(result.RefreshToken); AuthStateChanged?.Invoke(this, true); return true;
    }

    public async Task<bool> TryRefreshAsync(bool force = false)
    {
        await _refreshGate.WaitAsync();
        try { if (!force && IsAuthenticated) return true; return await RefreshCoreAsync(); }
        finally { _refreshGate.Release(); }
    }

    private async Task<bool> RefreshCoreAsync()
    {
        var savedToken = LoadRefreshToken();
        if (savedToken == null) return false;

        var client = _httpFactory.CreateClient("IFBAuth");
        try
        {
            var resp = await client.PostAsJsonAsync("api/v1/auth/refresh",
                new RefreshTokenRequestDto(savedToken));
            if (!resp.IsSuccessStatusCode) return false;

            var result = await resp.Content.ReadFromJsonAsync<LoginResponseDto>();
            if (result == null) return false;

            _accessToken = result.AccessToken;
            _refreshToken = result.RefreshToken;
            _tokenExpiry = result.ExpiresAt;
            CurrentUser = result.User;
            SaveRefreshToken(_refreshToken);
            return true;
        }
        catch { return false; }
    }

    // ── Windows Credential Manager storage ───────────────────────────────────
    private string CredentialTarget => "IFBConnect:" + _endpoint.Address.Authority + (ClientProfile.IsDefault ? "" : ":profile:" + ClientProfile.Name);
    private void SaveRefreshToken(string token) => CredentialStore.Save(CredentialTarget, token);
    private string? LoadRefreshToken() => CredentialStore.Load(CredentialTarget);
    private void DeleteRefreshToken() => CredentialStore.Remove(CredentialTarget);
}
// ═══════════════════════════════════════════════════════════════════════════════
// SignalR Hub Connection Service
// ═══════════════════════════════════════════════════════════════════════════════

public interface IHubConnectionService
{
    ConnectionState State { get; }
    event EventHandler<ConnectionState>? ConnectionStateChanged;

    // Hub method callers
    Task ConnectAsync();
    Task DisconnectAsync();
    Task SendMessageAsync(SendMessageDto dto);
    Task EditMessageAsync(EditMessageDto dto);
    Task DeleteMessageAsync(DeleteMessageDto dto);
    Task ToggleReactionAsync(ToggleReactionDto dto);
    Task MarkReadAsync(MarkReadDto dto);
    Task SendTypingAsync(TypingDto dto);
    Task UpdatePresenceAsync(UpdatePresenceDto dto);
    Task JoinConversationAsync(string conversationId);

    // Hub inbound events (server → client)
    event EventHandler<MessageDto>? MessageReceived;
    event EventHandler<MessageEditedDto>? MessageEdited;
    event EventHandler<MessageDeletedDto>? MessageDeleted;
    event EventHandler<ReactionUpdatedDto>? ReactionUpdated;
    event EventHandler<ReadReceiptDto>? ReadReceiptReceived;
    event EventHandler<TypingEventDto>? TypingReceived;
    event EventHandler<PresenceUpdateDto>? PresenceChanged;
    event EventHandler<ConversationDto>? ConversationUpdated;
    event EventHandler<AnnouncementDto>? AnnouncementPosted;
}

public sealed class HubConnectionService : IHubConnectionService, IAsyncDisposable
{
    private readonly IClientAuthService _auth;
    private readonly IConfiguration _config;
    private HubConnection? _connection;
    private readonly ServerEndpoint _endpoint;

    public ConnectionState State { get; private set; } = ConnectionState.Disconnected;
    public event EventHandler<ConnectionState>? ConnectionStateChanged;

    // Inbound events
    public event EventHandler<MessageDto>? MessageReceived;
    public event EventHandler<MessageEditedDto>? MessageEdited;
    public event EventHandler<MessageDeletedDto>? MessageDeleted;
    public event EventHandler<ReactionUpdatedDto>? ReactionUpdated;
    public event EventHandler<ReadReceiptDto>? ReadReceiptReceived;
    public event EventHandler<TypingEventDto>? TypingReceived;
    public event EventHandler<PresenceUpdateDto>? PresenceChanged;
    public event EventHandler<ConversationDto>? ConversationUpdated;
    public event EventHandler<AnnouncementDto>? AnnouncementPosted;

    public HubConnectionService(IClientAuthService auth, IConfiguration config, ServerEndpoint endpoint)
    {
        _auth = auth;
        _config = config;
        _endpoint = endpoint;
    }

    public async Task ConnectAsync()
    {
        if (_connection != null &&
            _connection.State == HubConnectionState.Connected) return;
        if (_connection != null) { await _connection.DisposeAsync(); _connection = null; }

        var host = _config["Server:Host"] ?? "localhost";
        var port = _config["Server:Port"] ?? "5001";
        var hubUrl = new Uri(_endpoint.Address, "hubs/chat");

        var maxAttempts = int.Parse(_config["Server:MaxReconnectAttempts"] ?? "20");
        var intervalMs = int.Parse(_config["Server:ReconnectIntervalMs"] ?? "2000");

        _connection = new HubConnectionBuilder()
            .WithUrl(hubUrl, opts =>
            {
                // Send JWT as query string for SignalR WebSocket handshake
                opts.AccessTokenProvider = async () =>
                { if (!_auth.IsAuthenticated) await _auth.TryRefreshAsync(); return _auth.AccessToken; };
                opts.HttpMessageHandlerFactory = _ => ServerEndpoint.CreateHandler();
                opts.WebSocketFactory = async (context, ct) =>
                {
                    var socket = new System.Net.WebSockets.ClientWebSocket();
                    socket.Options.HttpVersion = System.Net.HttpVersion.Version11;
                    socket.Options.HttpVersionPolicy = HttpVersionPolicy.RequestVersionExact;
                    var token = await context.Options.AccessTokenProvider!();
                    if (token != null) socket.Options.SetRequestHeader("Authorization", $"Bearer {token}");
                    using var invoker = new HttpMessageInvoker(ServerEndpoint.CreateHandler());
                    await socket.ConnectAsync(context.Uri, invoker, ct);
                    return socket;
                };
            })
            // Quick retries first, then keep trying every 30 seconds so a longer network or
            // server outage recovers on its own instead of needing a manual reconnect.
            .WithAutomaticReconnect(new PersistentRetryPolicy(maxAttempts, TimeSpan.FromMilliseconds(intervalMs)))
            .Build();

        // Lifecycle
        _connection.Reconnecting += _ => { SetState(ConnectionState.Reconnecting); return Task.CompletedTask; };
        _connection.Reconnected += _ => { SetState(ConnectionState.Connected); return Task.CompletedTask; };
        _connection.Closed += _ => { SetState(ConnectionState.Disconnected); return Task.CompletedTask; };

        // Register inbound handlers — names must match IClientHub method names
        _connection.On<MessageDto>(nameof(IClientHub.MessageReceivedAsync),
            dto => Raise(() => MessageReceived?.Invoke(this, dto)));

        _connection.On<MessageEditedDto>(nameof(IClientHub.MessageEditedAsync),
            dto => Raise(() => MessageEdited?.Invoke(this, dto)));

        _connection.On<MessageDeletedDto>(nameof(IClientHub.MessageDeletedAsync),
            dto => Raise(() => MessageDeleted?.Invoke(this, dto)));

        _connection.On<ReactionUpdatedDto>(nameof(IClientHub.ReactionUpdatedAsync),
            dto => Raise(() => ReactionUpdated?.Invoke(this, dto)));

        _connection.On<ReadReceiptDto>(nameof(IClientHub.ReadReceiptAsync),
            dto => Raise(() => ReadReceiptReceived?.Invoke(this, dto)));

        _connection.On<TypingEventDto>(nameof(IClientHub.TypingAsync),
            dto => Raise(() => TypingReceived?.Invoke(this, dto)));

        _connection.On<PresenceUpdateDto>(nameof(IClientHub.PresenceChangedAsync),
            dto => Raise(() => PresenceChanged?.Invoke(this, dto)));

        _connection.On<ConversationDto>(nameof(IClientHub.ConversationUpdatedAsync),
            dto => Raise(() => ConversationUpdated?.Invoke(this, dto)));

        _connection.On<AnnouncementDto>(nameof(IClientHub.AnnouncementPostedAsync),
            dto => Raise(() => AnnouncementPosted?.Invoke(this, dto)));

        _connection.On<string>(nameof(IClientHub.UserForceLogoutAsync),
            async reason => { await _auth.LogoutAsync(); await DisconnectAsync();
                App.Current.MainWindow?.Dispatch(() => App.Services.GetRequiredService<INavigationService>().NavigateTo("Login")); });

        SetState(ConnectionState.Connecting);
        try { await _connection.StartAsync(); } catch { SetState(ConnectionState.Disconnected); throw; }
        SetState(ConnectionState.Connected);
    }

    public async Task DisconnectAsync()
    {
        if (_connection != null)
        {
            await _connection.StopAsync();
            await _connection.DisposeAsync();
            _connection = null;
        }
        SetState(ConnectionState.Disconnected);
    }

    // ── Hub method callers ────────────────────────────────────────────────────

    public Task SendMessageAsync(SendMessageDto dto) =>
        InvokeAsync(nameof(IServerHub.SendMessageAsync), dto);

    public Task EditMessageAsync(EditMessageDto dto) =>
        InvokeAsync(nameof(IServerHub.EditMessageAsync), dto);

    public Task DeleteMessageAsync(DeleteMessageDto dto) =>
        InvokeAsync(nameof(IServerHub.DeleteMessageAsync), dto);

    public Task ToggleReactionAsync(ToggleReactionDto dto) =>
        InvokeAsync(nameof(IServerHub.ToggleReactionAsync), dto);

    public Task MarkReadAsync(MarkReadDto dto) =>
        InvokeAsync(nameof(IServerHub.MarkReadAsync), dto);

    public Task SendTypingAsync(TypingDto dto) =>
        InvokeAsync(nameof(IServerHub.SendTypingAsync), dto);

    public Task UpdatePresenceAsync(UpdatePresenceDto dto) =>
        InvokeAsync(nameof(IServerHub.UpdatePresenceAsync), dto);

    public Task JoinConversationAsync(string conversationId) =>
        InvokeAsync(nameof(IServerHub.JoinConversationAsync), conversationId);

    private Task InvokeAsync(string method, object? arg = null)
    {
        if (_connection == null ||
            _connection.State != HubConnectionState.Connected)
            throw new InvalidOperationException("Connection is offline. Reconnect before sending.");

        return arg == null
            ? _connection.InvokeAsync(method)
            : _connection.InvokeAsync(method, arg);
    }

    private static void Raise(Action action) { App.Current.MainWindow?.Dispatch(action); }
    private void SetState(ConnectionState state)
    {
        State = state;
        Raise(() => ConnectionStateChanged?.Invoke(this, state));
    }

    public async ValueTask DisposeAsync() => await DisconnectAsync();
}

/// <summary>Retries at the configured interval, then every 30 seconds indefinitely.</summary>
internal sealed class PersistentRetryPolicy(int quickAttempts, TimeSpan quickInterval) : IRetryPolicy
{
    private static readonly TimeSpan SlowInterval = TimeSpan.FromSeconds(30);
    public TimeSpan? NextRetryDelay(RetryContext retryContext) =>
        retryContext.PreviousRetryCount < quickAttempts ? quickInterval : SlowInterval;
}

// ═══════════════════════════════════════════════════════════════════════════════
// REST API Service
// ═══════════════════════════════════════════════════════════════════════════════

public record DiscoveryResult(string ServerUrl, string Version);
public record UserSearchResult(int Total, List<UserDto> Users);
public record MessagePage(List<MessageDto> Messages, bool HasMore);

public interface IApiService
{
    // Conversations
    Task<List<ConversationDto>> GetConversationsAsync();
    Task<ConversationDto> CreateDmAsync(string otherUserId);
    Task<ConversationDto> CreateGroupAsync(string name, string? description, List<string> memberIds);

    // Messages
    Task<MessagePage> GetMessageHistoryAsync(string conversationId,
        DateTime? before, int limit = 50, string? beforeId = null);

    // Users
    Task<UserSearchResult> SearchUsersAsync(string? query, string? department,
        int limit = 50, int offset = 0);
    Task<List<string>> GetDepartmentsAsync();

    // Announcements
    Task<List<AnnouncementDto>> GetAnnouncementsAsync();
    Task AcknowledgeAnnouncementAsync(string announcementId);
}

public sealed class ApiService : IApiService
{
    private readonly IHttpClientFactory _httpFactory;

    public ApiService(IHttpClientFactory httpFactory) => _httpFactory = httpFactory;

    private HttpClient Client => _httpFactory.CreateClient("IFBServer");

    public async Task<List<ConversationDto>> GetConversationsAsync()
    {
        var resp = await Client.GetFromJsonAsync<List<ConversationDto>>("api/v1/conversations");
        return resp ?? [];
    }

    public async Task<ConversationDto> CreateDmAsync(string otherUserId)
    {
        var resp = await Client.PostAsJsonAsync("api/v1/conversations/dm",
            new { otherUserId });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<ConversationDto>())!;
    }

    public async Task<ConversationDto> CreateGroupAsync(
        string name, string? description, List<string> memberIds)
    {
        var resp = await Client.PostAsJsonAsync("api/v1/conversations/group",
            new { name, description, memberIds });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<ConversationDto>())!;
    }

    public async Task<MessagePage> GetMessageHistoryAsync(
        string conversationId, DateTime? before, int limit = 50, string? beforeId = null)
    {
        var url = $"api/v1/conversations/{conversationId}/messages?limit={limit}";
        if (before.HasValue)
            url += $"&before={Uri.EscapeDataString(before.Value.ToString("O"))}";
        if (beforeId != null) url += "&beforeId=" + Uri.EscapeDataString(beforeId);

        var resp = await Client.GetFromJsonAsync<MessagePage>(url);
        return resp ?? new MessagePage([], false);
    }

    public async Task<UserSearchResult> SearchUsersAsync(
        string? query, string? department, int limit = 50, int offset = 0)
    {
        var url = $"api/v1/users?limit={limit}&offset={offset}";
        if (!string.IsNullOrWhiteSpace(query)) url += $"&q={Uri.EscapeDataString(query)}";
        if (!string.IsNullOrWhiteSpace(department)) url += $"&dept={Uri.EscapeDataString(department)}";

        var resp = await Client.GetFromJsonAsync<JsonElement>(url);
        var total = resp.GetProperty("total").GetInt32();
        var users = resp.GetProperty("users").Deserialize<List<UserDto>>(new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? [];
        return new UserSearchResult(total, users);
    }

    public async Task<List<string>> GetDepartmentsAsync()
    {
        var resp = await Client.GetFromJsonAsync<List<string>>("api/v1/users/departments");
        return resp ?? [];
    }

    public async Task<List<AnnouncementDto>> GetAnnouncementsAsync()
    {
        var resp = await Client.GetFromJsonAsync<List<AnnouncementDto>>("api/v1/announcements");
        return resp ?? [];
    }

    public async Task AcknowledgeAnnouncementAsync(string announcementId)
    {
        await Client.PostAsync($"api/v1/announcements/{announcementId}/acknowledge", null);
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
// UDP Discovery Service
// ═══════════════════════════════════════════════════════════════════════════════

public interface IDiscoveryService
{
    Task<DiscoveryResult?> DiscoverAsync(int timeoutMs = 3000,
        CancellationToken ct = default);
}

public sealed class DiscoveryService : IDiscoveryService
{
    private readonly IConfiguration _config;
    public DiscoveryService(IConfiguration config) => _config = config;

    public async Task<DiscoveryResult?> DiscoverAsync(int timeoutMs = 3000,
        CancellationToken ct = default)
    {
        var udpPort = int.Parse(_config["Server:DiscoveryUdpPort"] ?? "5500");
        try
        {
            using var udp = new System.Net.Sockets.UdpClient();
            udp.EnableBroadcast = true;

            var payload = System.Text.Encoding.UTF8.GetBytes("IFB_DISCOVER_v1");
            await udp.SendAsync(payload,
                new System.Net.IPEndPoint(System.Net.IPAddress.Broadcast, udpPort), ct);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeoutMs);

            var result = await udp.ReceiveAsync(cts.Token);
            var json = System.Text.Encoding.UTF8.GetString(result.Buffer);
            var doc = JsonDocument.Parse(json);

            var host = doc.RootElement.GetProperty("host").GetString();
            var port = doc.RootElement.GetProperty("port").GetInt32();
            var version = doc.RootElement.TryGetProperty("version", out var v)
                ? v.GetString() : "1.0";

            if (host == null) return null;
            return new DiscoveryResult($"https://{host}:{port}", version ?? "1.0");
        }
        catch { return null; }
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
// Theme Service
// ═══════════════════════════════════════════════════════════════════════════════

public interface IThemeService
{
    bool IsPerformanceMode { get; }
    string CurrentTheme { get; }
    void SetTheme(string theme);
    void SetPerformanceMode(bool enabled);
    event EventHandler<bool> PerformanceModeChanged;
    event EventHandler<string> ThemeChanged;
}

public sealed class ThemeService : IThemeService
{
    public bool IsPerformanceMode { get; private set; }
    public string CurrentTheme { get; private set; } = "Auto";

    public event EventHandler<bool>? PerformanceModeChanged;
    public event EventHandler<string>? ThemeChanged;

    public void SetTheme(string theme)
    {
        CurrentTheme = theme;
        ThemeChanged?.Invoke(this, theme);
    }

    public void SetPerformanceMode(bool enabled)
    {
        IsPerformanceMode = enabled;
        PerformanceModeChanged?.Invoke(this, enabled);
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
// Navigation Service
// ═══════════════════════════════════════════════════════════════════════════════

public interface INavigationService
{
    void NavigateTo(string page, object? parameter = null);
    event EventHandler<(string Page, object? Parameter)> NavigationRequested;
}

public sealed class NavigationService : INavigationService
{
    public event EventHandler<(string Page, object? Parameter)>? NavigationRequested;

    public void NavigateTo(string page, object? parameter = null) =>
        NavigationRequested?.Invoke(this, (page, parameter));
}

// ═══════════════════════════════════════════════════════════════════════════════
// Notification Service
// ═══════════════════════════════════════════════════════════════════════════════

public interface INotificationService
{
    void ShowToast(string title, string message, string? conversationId = null);
    void SetDoNotDisturb(bool enabled);
    bool IsDoNotDisturb { get; }
}

public sealed class NotificationService : INotificationService
{
    private readonly ClientPreferences _preferences;
    public NotificationService(ClientPreferences preferences) { _preferences = preferences; _doNotDisturb = preferences.DoNotDisturb; }
    private bool _doNotDisturb;
    public bool IsDoNotDisturb => _doNotDisturb;

    public void ShowToast(string title, string message, string? conversationId = null)
    {
        if (_doNotDisturb || !_preferences.Notifications) return;
        try
        {
            var builder = new Microsoft.Windows.AppNotifications.Builder.AppNotificationBuilder().AddText(title).AddText(message);
            if (conversationId != null) builder.AddArgument("conversationId", conversationId);
            if (!_preferences.Sounds) builder.MuteAudio();
            Microsoft.Windows.AppNotifications.AppNotificationManager.Default.Show(builder.BuildNotification());
        }
        catch { App.Current.Tray?.Balloon(title, message); }
    }

    public void SetDoNotDisturb(bool enabled) => _doNotDisturb = enabled;
}




using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using IFB.Server.Data;
using IFB.Server.Middleware;
using IFB.Server.Models;
using IFB.Server.Services;
using IFB.Shared.Contracts;
using IFB.Shared.DTOs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
var fixture = Path.Combine(root, ".work", "tests", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(fixture);
var passed = new List<string>();
void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL: " + name); passed.Add(name); Console.WriteLine("PASS: " + name); }
using var rsa = RSA.Create(2048);
var certRequest = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
var names = new SubjectAlternativeNameBuilder(); names.AddDnsName("localhost"); names.AddIpAddress(IPAddress.Loopback); certRequest.CertificateExtensions.Add(names.Build());
using var certificate = certRequest.CreateSelfSigned(DateTimeOffset.Now.AddMinutes(-5), DateTimeOffset.Now.AddDays(1));
var certificatePath = Path.Combine(fixture, "server.pfx"); await File.WriteAllBytesAsync(certificatePath, certificate.Export(X509ContentType.Pfx));
var keyPath = Path.Combine(fixture, "jwt.pem"); await File.WriteAllTextAsync(keyPath, rsa.ExportRSAPrivateKeyPem());
File.Copy(Path.Combine(root, "src", "IFB.Server", "appsettings.json"), Path.Combine(fixture, "appsettings.json"));
var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
var server = Path.Combine(AppContext.BaseDirectory, "IFB.Server.dll");
var password = "Test-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
var start = new ProcessStartInfo("dotnet") { WorkingDirectory = fixture, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
start.ArgumentList.Add(server);
foreach (var pair in new Dictionary<string, string> {
    ["ASPNETCORE_ENVIRONMENT"] = "Production", ["Server__BindAddress"] = "127.0.0.1", ["Server__Port"] = port.ToString(),
    ["Server__CertificatePath"] = certificatePath, ["Jwt__RsaPrivateKeyPath"] = keyPath,
    ["Bootstrap__AdminUsername"] = "admin", ["Bootstrap__AdminPassword"] = password,
    ["Auth__Mode"] = "Local", ["Discovery__Enabled"] = "false", ["FileStorage__BasePath"] = Path.Combine(fixture, "files"),
    ["FileStorage__AntivirusCommand"] = "missing-scanner.exe", ["FileStorage__SkipAvScanIfMissing"] = "true",
    ["RateLimiting__LoginAttemptsPerMinute"] = "100", ["RateLimiting__MessagesPerMinutePerUser"] = "3" }) start.Environment[pair.Key] = pair.Value;
using var process = Process.Start(start)!;
Console.WriteLine($"Isolated HTTPS server process {process.Id} on local port {port}");
var serverOutput = process.StandardOutput.ReadToEndAsync(); var serverErrors = process.StandardError.ReadToEndAsync();
HttpClient Client(string? token = null)
{
    var client = new HttpClient(new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, c, _, _) => c?.Thumbprint == certificate.Thumbprint }) { BaseAddress = new Uri($"https://localhost:{port}"), Timeout = TimeSpan.FromSeconds(10) };
    if (token != null) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token); return client;
}
async Task<LoginResponseDto> Login(string username)
{ using var c = Client(); using var r = await c.PostAsJsonAsync("api/v1/auth/login", new LoginRequestDto(username, password)); r.EnsureSuccessStatusCode(); return (await r.Content.ReadFromJsonAsync<LoginResponseDto>())!; }
async Task<HttpResponseMessage> Post(HttpClient c, string path, object body) => await c.PostAsJsonAsync(path, body);
HubConnection Hub(string token)
{
    return new HubConnectionBuilder().WithUrl($"https://localhost:{port}/hubs/chat", o =>
    { o.AccessTokenProvider = () => Task.FromResult<string?>(token);
      o.HttpMessageHandlerFactory = _ => new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, c, _, _) => c?.Thumbprint == certificate.Thumbprint };
      o.WebSocketConfiguration = ws => ws.RemoteCertificateValidationCallback = (_, c, _, _) => c?.GetCertHashString() == certificate.Thumbprint; }).Build();
}
try
{
    using var anonymous = Client();
    var ready = false;
    for (var attempt = 0; attempt < 100; attempt++)
    { if (process.HasExited) throw new Exception("Server exited before readiness."); try { ready = (await anonymous.GetAsync("health")).IsSuccessStatusCode; } catch (HttpRequestException) { } if (ready) break; await Task.Delay(100); }
    Check(ready, "Fresh database migrations and administrator bootstrap start successfully");
    Check((await anonymous.GetAsync("api/v1/users")).StatusCode == HttpStatusCode.Unauthorized, "Anonymous directory access is rejected");
    var admin = await Login("admin"); using var adminClient = Client(admin.AccessToken);
    Check((await adminClient.GetAsync("api/v1/auth/me")).IsSuccessStatusCode, "Issued RSA token validates against server signing key");
    foreach (var username in new[] { "alice", "bob", "eve" })
    { using var response = await Post(adminClient, "api/v1/users", new { username, password, displayName = username, email = username + "@test.local", role = 0 }); Check(response.IsSuccessStatusCode, "Administrator creates " + username); }
    var alice = await Login("alice"); var bob = await Login("bob"); var eve = await Login("eve");
    using var aliceClient = Client(alice.AccessToken); using var bobClient = Client(bob.AccessToken); using var eveClient = Client(eve.AccessToken);
    Check((await eveClient.GetAsync("api/v1/admin/users")).StatusCode == HttpStatusCode.Forbidden, "Employee cannot access administration");
    using var dmResponse = await Post(aliceClient, "api/v1/conversations/dm", new { otherUserId = bob.User.Id }); dmResponse.EnsureSuccessStatusCode();
    var dm = (await dmResponse.Content.ReadFromJsonAsync<ConversationDto>())!;
    Check(dm.Members.Count == 2, "Direct conversation has both members");
    await using var aliceHub = Hub(alice.AccessToken); await aliceHub.StartAsync();
    var aliceMessage = new TaskCompletionSource<MessageDto>(TaskCreationOptions.RunContinuationsAsynchronously);
    aliceHub.On<MessageDto>(nameof(IClientHub.MessageReceivedAsync), m => aliceMessage.TrySetResult(m));
    await aliceHub.InvokeAsync(nameof(IServerHub.SendMessageAsync), new SendMessageDto(dm.Id, "Offline delivery test", IFB.Shared.Enums.MessageType.Text, null, null));
    var message = await aliceMessage.Task.WaitAsync(TimeSpan.FromSeconds(5));
    Check(message.Content == "Offline delivery test", "SignalR sends and persists a real message");
    var aliceConversations = (await aliceClient.GetFromJsonAsync<List<ConversationDto>>("api/v1/conversations"))!;
    var bobConversations = (await bobClient.GetFromJsonAsync<List<ConversationDto>>("api/v1/conversations"))!;
    var eveConversations = (await eveClient.GetFromJsonAsync<List<ConversationDto>>("api/v1/conversations"))!;
    Check(aliceConversations.Any(c => c.Id == dm.Id && c.LastMessage?.Id == message.Id && c.UnreadCount == 0), "Conversation list returns the latest preview without counting the sender's own message as unread");
    Check(bobConversations.Any(c => c.Id == dm.Id && c.UnreadCount == 1), "Recipient's conversation list includes unread incoming messages");
    Check(eveConversations.All(c => c.Id != dm.Id), "Conversation list excludes conversations the employee has not joined");
    await using var bobHub = Hub(bob.AccessToken); var offlineMessage = new TaskCompletionSource<MessageDto>(TaskCreationOptions.RunContinuationsAsynchronously);
    bobHub.On<MessageDto>(nameof(IClientHub.MessageReceivedAsync), m => offlineMessage.TrySetResult(m)); await bobHub.StartAsync();
    Check((await offlineMessage.Task.WaitAsync(TimeSpan.FromSeconds(5))).Id == message.Id, "Offline queue delivers on reconnect");
    var replyReceived = new TaskCompletionSource<MessageDto>(TaskCreationOptions.RunContinuationsAsynchronously);
    aliceHub.On<MessageDto>(nameof(IClientHub.MessageReceivedAsync), m => { if (m.Content == "Reply from Bob") replyReceived.TrySetResult(m); });
    await bobHub.InvokeAsync(nameof(IServerHub.SendMessageAsync), new SendMessageDto(dm.Id, "Reply from Bob", IFB.Shared.Enums.MessageType.Text, message.Id, null));
    var reply = await replyReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
    Check(reply.SenderId == bob.User.Id && reply.ReplyToId == message.Id, "Two employee sessions deliver a reply in the reverse direction");
    var replyHistory = await aliceClient.GetFromJsonAsync<JsonElement>($"api/v1/conversations/{dm.Id}/messages");
    Check(replyHistory.GetProperty("messages").EnumerateArray().Any(m => m.GetProperty("id").GetString() == reply.Id && m.GetProperty("replyToId").GetString() == message.Id), "Reply relationship persists in history");
    var reactionAdded = new TaskCompletionSource<ReactionUpdatedDto>(TaskCreationOptions.RunContinuationsAsynchronously);
    var reactionRemoved = new TaskCompletionSource<ReactionUpdatedDto>(TaskCreationOptions.RunContinuationsAsynchronously);
    aliceHub.On<ReactionUpdatedDto>(nameof(IClientHub.ReactionUpdatedAsync), r => { if (r.MessageId == message.Id) { if (r.Added) reactionAdded.TrySetResult(r); else reactionRemoved.TrySetResult(r); } });
    await bobHub.InvokeAsync(nameof(IServerHub.ToggleReactionAsync), new ToggleReactionDto(message.Id, 0));
    Check((await reactionAdded.Task.WaitAsync(TimeSpan.FromSeconds(5))).UserId == bob.User.Id, "Reaction additions broadcast to the other employee session");
    await bobHub.InvokeAsync(nameof(IServerHub.ToggleReactionAsync), new ToggleReactionDto(message.Id, 0));
    Check(!(await reactionRemoved.Task.WaitAsync(TimeSpan.FromSeconds(5))).Added, "Reaction toggle removes and broadcasts the reaction");
    var typing = new TaskCompletionSource<TypingEventDto>(TaskCreationOptions.RunContinuationsAsynchronously);
    aliceHub.On<TypingEventDto>(nameof(IClientHub.TypingAsync), t => typing.TrySetResult(t));
    await bobHub.InvokeAsync(nameof(IServerHub.SendTypingAsync), new TypingDto(dm.Id, true));
    Check((await typing.Task.WaitAsync(TimeSpan.FromSeconds(5))).IsTyping, "Typing status reaches the other employee session");
    var png = await File.ReadAllBytesAsync(Path.Combine(root, "tests", "fixtures", "qa-preview.png"));
    using var imageContent = new MultipartFormDataContent(); imageContent.Add(new StringContent(dm.Id), "conversationId"); imageContent.Add(new ByteArrayContent(png), "file", "preview.png");
    using var imageUpload = await bobClient.PostAsync("api/v1/files/upload", imageContent); imageUpload.EnsureSuccessStatusCode(); var imageFile = await imageUpload.Content.ReadFromJsonAsync<JsonElement>();
    var imageId = imageFile.GetProperty("id").GetString();
    using var imageDownload = await aliceClient.GetAsync($"api/v1/files/{imageId}");
    Check(imageDownload.IsSuccessStatusCode && imageDownload.Content.Headers.ContentType?.MediaType == "image/png" && (await imageDownload.Content.ReadAsByteArrayAsync()).SequenceEqual(png), "Shared PNG preview payload has the correct media type and unchanged bytes");
    var imageMessage = new TaskCompletionSource<MessageDto>(TaskCreationOptions.RunContinuationsAsynchronously);
    aliceHub.On<MessageDto>(nameof(IClientHub.MessageReceivedAsync), m => { if (m.FileId == imageId) imageMessage.TrySetResult(m); });
    await bobHub.InvokeAsync(nameof(IServerHub.SendMessageAsync), new SendMessageDto(dm.Id, "preview.png", IFB.Shared.Enums.MessageType.File, null, imageId));
    Check((await imageMessage.Task.WaitAsync(TimeSpan.FromSeconds(5))).FileId == imageId, "Image attachment references reach the other employee session");
    Check((await eveClient.GetAsync($"api/v1/conversations/{dm.Id}/messages")).StatusCode == HttpStatusCode.Forbidden, "Non-member cannot read message history");
    await using var eveHub = Hub(eve.AccessToken); await eveHub.StartAsync();
    await eveHub.InvokeAsync(nameof(IServerHub.MarkReadAsync), new MarkReadDto(message.Id));
    using var otherDmResponse = await Post(eveClient, "api/v1/conversations/dm", new { otherUserId = admin.User.Id }); otherDmResponse.EnsureSuccessStatusCode(); var otherDm = (await otherDmResponse.Content.ReadFromJsonAsync<ConversationDto>())!;
    Check((await Post(eveClient, $"api/v1/conversations/{otherDm.Id}/messages/read", new MarkReadDto(message.Id))).StatusCode == HttpStatusCode.NotFound, "Read receipt cannot target a message in another conversation");
    Check((await Post(eveClient, $"api/v1/conversations/{otherDm.Id}/messages/{message.Id}/react", new { emoji = 0 })).StatusCode == HttpStatusCode.NotFound, "Reaction cannot target a message in another conversation");
    Check((await Post(aliceClient, $"api/v1/conversations/{dm.Id}/messages/{message.Id}/react", new { emoji = 999 })).StatusCode == HttpStatusCode.BadRequest, "Undefined reactions are rejected");
    var edited = new TaskCompletionSource<MessageEditedDto>(TaskCreationOptions.RunContinuationsAsynchronously);
    bobHub.On<MessageEditedDto>(nameof(IClientHub.MessageEditedAsync), m => edited.TrySetResult(m));
    using var editResponse = await aliceClient.PatchAsJsonAsync($"api/v1/conversations/{dm.Id}/messages/{message.Id}", new { newContent = "Edited searchable message" });
    Check(editResponse.IsSuccessStatusCode && (await edited.Task.WaitAsync(TimeSpan.FromSeconds(5))).NewContent == "Edited searchable message", "REST edits broadcast to connected conversation members");
    var searchResults = await bobClient.GetFromJsonAsync<JsonElement>($"api/v1/conversations/{dm.Id}/messages?q=searchable");
    Check(searchResults.GetProperty("messages").GetArrayLength() == 1, "Conversation history search finds edited content");
    Check((await eveClient.DeleteAsync($"api/v1/conversations/{dm.Id}/messages/{message.Id}")).StatusCode == HttpStatusCode.Forbidden, "Non-member cannot delete another conversation's message");
    using var groupResponse = await Post(aliceClient, "api/v1/conversations/group", new { name = "Membership test", memberIds = new[] { bob.User.Id } });
    groupResponse.EnsureSuccessStatusCode(); var group = (await groupResponse.Content.ReadFromJsonAsync<ConversationDto>())!;
    Check(group.Members.Count == 2, "Group creation persists the selected members");
    Check((await adminClient.GetAsync($"api/v1/conversations/{group.Id}/members")).IsSuccessStatusCode, "Global administrator can inspect group membership");
    Check((await aliceClient.DeleteAsync($"api/v1/conversations/{group.Id}/leave")).StatusCode == HttpStatusCode.BadRequest, "Last group administrator cannot accidentally abandon the group");
    Check((await bobClient.DeleteAsync($"api/v1/conversations/{group.Id}/leave")).IsSuccessStatusCode &&
        (await bobClient.GetAsync($"api/v1/conversations/{group.Id}/messages")).StatusCode == HttpStatusCode.Forbidden, "Leaving a group immediately removes history access");
    using var content = new MultipartFormDataContent(); content.Add(new StringContent(dm.Id), "conversationId"); content.Add(new ByteArrayContent("verified attachment"u8.ToArray()), "file", "check.txt");
    using var upload = await aliceClient.PostAsync("api/v1/files/upload", content); upload.EnsureSuccessStatusCode(); var file = await upload.Content.ReadFromJsonAsync<JsonElement>(); var fileId = file.GetProperty("id").GetString();
    using var download = await bobClient.GetAsync($"api/v1/files/{fileId}"); download.EnsureSuccessStatusCode(); var bytes = await download.Content.ReadAsByteArrayAsync();
    Check(Convert.ToHexString(SHA256.HashData(bytes)).Equals(file.GetProperty("sha256Hash").GetString(), StringComparison.OrdinalIgnoreCase), "File download matches upload SHA-256");
    Check((await eveClient.GetAsync($"api/v1/files/{fileId}")).StatusCode == HttpStatusCode.Forbidden, "Non-member file download is rejected");
    using var forbiddenFile = new MultipartFormDataContent(); forbiddenFile.Add(new StringContent(dm.Id), "conversationId"); forbiddenFile.Add(new ByteArrayContent([1]), "file", "bad.exe");
    Check((await aliceClient.PostAsync("api/v1/files/upload", forbiddenFile)).StatusCode == HttpStatusCode.BadRequest, "Disallowed file extensions are rejected");
    var channelList = await aliceClient.GetFromJsonAsync<List<JsonElement>>("api/v1/conversations/channels"); Check(channelList!.Count >= 5, "Default department channels are available");
    var channelId = channelList[0].GetProperty("id").GetString(); Check((await Post(aliceClient, $"api/v1/conversations/{channelId}/join", new { })).IsSuccessStatusCode, "Employee can join a department channel");
    var announcement = new TaskCompletionSource<AnnouncementDto>(TaskCreationOptions.RunContinuationsAsynchronously); aliceHub.On<AnnouncementDto>(nameof(IClientHub.AnnouncementPostedAsync), a => announcement.TrySetResult(a));
    var bobAnnouncement = new TaskCompletionSource<AnnouncementDto>(TaskCreationOptions.RunContinuationsAsynchronously); bobHub.On<AnnouncementDto>(nameof(IClientHub.AnnouncementPostedAsync), a => bobAnnouncement.TrySetResult(a));
    using var postAnnouncement = await Post(adminClient, "api/v1/announcements", new { title = "Shift notice", content = "Test broadcast", priority = 2 }); postAnnouncement.EnsureSuccessStatusCode();
    var alert = await announcement.Task.WaitAsync(TimeSpan.FromSeconds(5)); Check(alert.Title == "Shift notice", "Priority announcement broadcasts in real time");
    Check((await bobAnnouncement.Task.WaitAsync(TimeSpan.FromSeconds(5))).Id == alert.Id, "Both employee sessions receive the same announcement");
    await Post(aliceClient, $"api/v1/announcements/{alert.Id}/acknowledge", new { }); var alerts = await aliceClient.GetFromJsonAsync<List<AnnouncementDto>>("api/v1/announcements"); Check(alerts!.Any(a => a.Id == alert.Id && a.IsAcknowledged), "Announcement acknowledgement persists");
    await aliceHub.InvokeAsync(nameof(IServerHub.SendMessageAsync), new SendMessageDto(dm.Id, "second", IFB.Shared.Enums.MessageType.Text, null, null));
    await aliceHub.InvokeAsync(nameof(IServerHub.SendMessageAsync), new SendMessageDto(dm.Id, "third", IFB.Shared.Enums.MessageType.Text, null, null));
    bool limited = false; try { await aliceHub.InvokeAsync(nameof(IServerHub.SendMessageAsync), new SendMessageDto(dm.Id, "fourth", IFB.Shared.Enums.MessageType.Text, null, null)); } catch (Microsoft.AspNetCore.SignalR.HubException) { limited = true; }
    Check(limited, "Per-user message rate limit is enforced on hub calls");
    using var refresh = await Post(anonymous, "api/v1/auth/refresh", new RefreshTokenRequestDto(bob.RefreshToken)); refresh.EnsureSuccessStatusCode(); var rotated = (await refresh.Content.ReadFromJsonAsync<LoginResponseDto>())!;
    Check((await Post(anonymous, "api/v1/auth/refresh", new RefreshTokenRequestDto(bob.RefreshToken))).StatusCode == HttpStatusCode.Unauthorized, "Refresh token is single-use after rotation");
    await Post(adminClient, $"api/v1/admin/users/{bob.User.Id}/logout", new { });
    Check((await bobClient.GetAsync("api/v1/auth/me")).StatusCode == HttpStatusCode.Unauthorized, "Force logout invalidates existing access token");
    Check((await Post(anonymous, "api/v1/auth/refresh", new RefreshTokenRequestDto(rotated.RefreshToken))).StatusCode == HttpStatusCode.Unauthorized, "Force logout revokes refresh tokens");
    Check((await adminClient.GetAsync("api/v1/admin/audit/export")).IsSuccessStatusCode, "Administrator can export audit CSV");
    // Read receipts: history is side-effect free and one call marks everything up to a message.
    await using var adminHub = Hub(admin.AccessToken);
    var receipts = new List<ReadReceiptDto>(); adminHub.On<ReadReceiptDto>(nameof(IClientHub.ReadReceiptAsync), r => { lock (receipts) receipts.Add(r); });
    await adminHub.StartAsync();
    await adminHub.InvokeAsync(nameof(IServerHub.SendMessageAsync), new SendMessageDto(otherDm.Id, "first unread", IFB.Shared.Enums.MessageType.Text, null, null));
    using var adminFileContent = new MultipartFormDataContent(); adminFileContent.Add(new StringContent(otherDm.Id), "conversationId"); adminFileContent.Add(new ByteArrayContent("to be deleted"u8.ToArray()), "file", "remove-me.txt");
    using var adminUpload = await adminClient.PostAsync("api/v1/files/upload", adminFileContent); adminUpload.EnsureSuccessStatusCode();
    var removableFileId = (await adminUpload.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();
    await adminHub.InvokeAsync(nameof(IServerHub.SendMessageAsync), new SendMessageDto(otherDm.Id, "remove-me.txt", IFB.Shared.Enums.MessageType.File, null, removableFileId));
    var eveHistory = await eveClient.GetFromJsonAsync<JsonElement>($"api/v1/conversations/{otherDm.Id}/messages");
    var newestId = eveHistory.GetProperty("messages").EnumerateArray().Last().GetProperty("id").GetString()!;
    var eveUnread = (await eveClient.GetFromJsonAsync<List<ConversationDto>>("api/v1/conversations"))!.First(c => c.Id == otherDm.Id).UnreadCount;
    Check(eveUnread == 2, "Loading history does not mark messages read");
    Check((await Post(eveClient, $"api/v1/conversations/{otherDm.Id}/messages/read", new MarkReadDto(newestId))).IsSuccessStatusCode &&
        (await eveClient.GetFromJsonAsync<List<ConversationDto>>("api/v1/conversations"))!.First(c => c.Id == otherDm.Id).UnreadCount == 0, "One read call marks all earlier incoming messages read");
    await Task.Delay(300);
    lock (receipts) Check(receipts.Count == 1 && receipts[0].MessageId == newestId, "Bulk read sends a single read-through receipt");
    Check((await adminClient.DeleteAsync($"api/v1/conversations/{otherDm.Id}/messages/{newestId}")).IsSuccessStatusCode &&
        (await eveClient.GetAsync($"api/v1/files/{removableFileId}")).StatusCode == HttpStatusCode.NotFound, "Deleting a message removes its unshared attachment");
    // Sign-out revokes the refresh token even without a valid access token.
    var eveSecond = await Login("eve");
    Check((await Post(anonymous, "api/v1/auth/logout", new RefreshTokenRequestDto(eveSecond.RefreshToken))).IsSuccessStatusCode &&
        (await Post(anonymous, "api/v1/auth/refresh", new RefreshTokenRequestDto(eveSecond.RefreshToken))).StatusCode == HttpStatusCode.Unauthorized, "Logout revokes the refresh token server-side");
    // Identity fields are administrator-managed.
    using var renameSelf = await eveClient.PatchAsJsonAsync("api/v1/users/me/profile", new { displayName = "Administrator" });
    Check(renameSelf.StatusCode == HttpStatusCode.Forbidden, "Employees cannot change their own display name");
    using var titleSelf = await eveClient.PatchAsJsonAsync("api/v1/users/me/profile", new { title = "Line supervisor" });
    Check(titleSelf.IsSuccessStatusCode, "Employees can update their own job title");
    using var adminRename = await adminClient.PatchAsJsonAsync($"api/v1/admin/users/{eve.User.Id}", new { role = 0, isActive = true, displayName = "Eve Example", title = "Line supervisor" });
    var eveMe = await eveClient.GetFromJsonAsync<UserDto>("api/v1/users/me");
    Check(adminRename.IsSuccessStatusCode && eveMe?.DisplayName == "Eve Example", "Administrator profile edits apply without ending the user's session");
    Check((await Post(adminClient, "api/v1/users", new { username = "bad name!", password, displayName = "Bad", email = "bad@test.local", role = 0 })).StatusCode == HttpStatusCode.BadRequest, "Local usernames are validated like sign-in names");
    var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["LanSecurity:RejectOutsideRanges"] = "true" }).Build();
    var lan = new LanGuardMiddleware(_ => Task.CompletedTask, config, NullLogger<LanGuardMiddleware>.Instance);
    var context = new DefaultHttpContext(); context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.10"); context.Response.Body = new MemoryStream(); await lan.InvokeAsync(context);
    Check(context.Response.StatusCode == 403, "LAN middleware blocks an outside address");
    Check(!LanBinding.IsPrivate(IPAddress.Any) && !LanBinding.IsPrivate(IPAddress.Parse("8.8.8.8")), "Wildcard and public bindings are rejected");
    await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite("Data Source=" + Path.Combine(fixture, "ifb_connect.db")).Options);
    Check(!await db.MessageReadReceipts.AnyAsync(r => r.UserId == eve.User.Id && r.MessageId == message.Id), "Unauthorized hub read receipt has no database effect");
    Check(await db.RefreshTokens.AllAsync(t => t.Token.Length == 64), "Database stores hashed refresh tokens only");
    if (args.Contains("--load"))
    {
        Console.WriteLine("Connecting 500 concurrent clients for the load check...");
        var users = Enumerable.Range(0, 500).Select(i => new User { Username = "load" + i, DisplayName = "Load " + i, Email = "load" + i + "@test.local" }).ToArray();
        var conversations = Enumerable.Range(0, 250).Select(i => new Conversation { Name = "Load pair " + i, Type = IFB.Shared.Enums.ConversationType.DirectMessage,
            Members = [new ConversationMember { UserId = users[i * 2].Id }, new ConversationMember { UserId = users[i * 2 + 1].Id }] }).ToArray();
        db.Users.AddRange(users); db.Conversations.AddRange(conversations); await db.SaveChangesAsync();
        string Token(User user) => new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().WriteToken(new System.IdentityModel.Tokens.Jwt.JwtSecurityToken(
            issuer: "IFBConnect", audience: "IFBConnectClients", claims: [new("sub", user.Id), new("token_version", "0"), new("name", user.DisplayName)], expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: new Microsoft.IdentityModel.Tokens.SigningCredentials(new Microsoft.IdentityModel.Tokens.RsaSecurityKey(rsa), Microsoft.IdentityModel.Tokens.SecurityAlgorithms.RsaSha256)));
        var connections = users.Select(u => Hub(Token(u))).ToArray(); var clocks = Enumerable.Range(0, 250).Select(_ => new Stopwatch()).ToArray();
        var completions = Enumerable.Range(0, 250).Select(_ => new TaskCompletionSource<double>(TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();
        for (var i = 0; i < 250; i++) { var pair = i; connections[i * 2 + 1].On<MessageDto>(nameof(IClientHub.MessageReceivedAsync), m => { if (m.Content == "load:" + pair) completions[pair].TrySetResult(clocks[pair].Elapsed.TotalMilliseconds); }); }
        using var connectGate = new SemaphoreSlim(20);
        try
        {
            await Task.WhenAll(connections.Select(async connection => { await connectGate.WaitAsync(); try { await connection.StartAsync(); } finally { connectGate.Release(); } }));
            Check(connections.Count(c => c.State == HubConnectionState.Connected) == 500, "500 simultaneous authenticated SignalR clients connect");
            using var sendGate = new SemaphoreSlim(25);
            await Task.WhenAll(Enumerable.Range(0, 250).Select(async i => { await sendGate.WaitAsync(); try { clocks[i].Start(); await connections[i * 2].InvokeAsync(nameof(IServerHub.SendMessageAsync),
                new SendMessageDto(conversations[i].Id, "load:" + i, IFB.Shared.Enums.MessageType.Text, null, null)); await completions[i].Task.WaitAsync(TimeSpan.FromSeconds(20)); } finally { sendGate.Release(); } }));
            var latencies = completions.Select(c => c.Task.Result).Order().ToArray(); var p95 = latencies[(int)(latencies.Length * .95)];
            Console.WriteLine($"500 clients, 250 deliveries: median {latencies[125]:F1} ms, p95 {p95:F1} ms, max {latencies[^1]:F1} ms");
            await File.WriteAllTextAsync(Path.Combine(root, "docs", "load-results.json"), JsonSerializer.Serialize(new { clients = 500, deliveries = 250, concurrentSends = 25, medianMs = latencies[125], p95Ms = p95, maxMs = latencies[^1], testedAtUtc = DateTime.UtcNow }, new JsonSerializerOptions { WriteIndented = true }));
            Check(p95 < 1000, "95% of deliveries arrive within one second with 500 connected clients");
        }
        finally { await Task.WhenAll(connections.Select(async c => { await c.DisposeAsync(); })); }
    }
    Console.WriteLine($"\n{passed.Count} checks passed.");
    Directory.CreateDirectory(Path.Combine(root, "docs")); await File.WriteAllTextAsync(Path.Combine(root, "docs", "test-results.json"), JsonSerializer.Serialize(new { testedAtUtc = DateTime.UtcNow, passed }, new JsonSerializerOptions { WriteIndented = true }));
}
finally
{
    if (!process.HasExited) process.Kill(true); await process.WaitForExitAsync();
    await File.WriteAllTextAsync(Path.Combine(fixture, "server.log"), await serverOutput + await serverErrors);
    Console.WriteLine("Isolated test data and server log: " + fixture);
}

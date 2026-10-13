using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using IFB.Server.Auth;
using IFB.Server.Data;
using IFB.Server.Models;
using IFB.Shared.Contracts;
using IFB.Shared.DTOs;
using IFB.Shared.Enums;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;

string Argument(string key) { var index = Array.IndexOf(args, key); return index >= 0 && index + 1 < args.Length ? args[index + 1] : throw new ArgumentException("Required: " + key); }
var database = Path.GetFullPath(Argument("--database"));
var output = Path.GetFullPath(Argument("--output"));
var server = new Uri(Argument("--server"));
if (!server.IsLoopback || server.Scheme != "https") throw new ArgumentException("This development acceptance helper only supports a local HTTPS server.");
if (!File.Exists(database)) throw new FileNotFoundException("Start the local development server first.", database);
Directory.CreateDirectory(output);
using var anonymous = new HttpClient { BaseAddress = server, Timeout = TimeSpan.FromSeconds(75) };
(await anonymous.GetAsync("health")).EnsureSuccessStatusCode();
await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite("Data Source=" + database).Options);
var accountFile = Path.Combine(output, "accounts.json");
List<TestAccount> accounts;
if (File.Exists(accountFile)) accounts = JsonSerializer.Deserialize<List<TestAccount>>(await File.ReadAllTextAsync(accountFile))!;
else
{
    accounts = [];
    foreach (var name in new[] { "alice", "bob" })
    {
        var username = "qa." + name;
        if (await db.Users.AnyAsync(u => u.Username == username)) username += "." + Guid.NewGuid().ToString("N")[..4];
        var password = "Qa!" + Convert.ToHexString(RandomNumberGenerator.GetBytes(9));
        var hash = AuthService.HashPassword(password);
        var user = new User { Username = username, DisplayName = name == "alice" ? "Alice QA" : "Bob QA",
            Email = username + "@test.local", Department = "QA Testing", IsLocalAccount = true,
            PasswordHash = hash.Hash, PasswordSalt = hash.Salt, Role = UserRole.Employee };
        db.Users.Add(user);
        accounts.Add(new TestAccount(user.Id, username, password, "qa-" + name));
    }
    await db.SaveChangesAsync();
    await File.WriteAllTextAsync(accountFile, JsonSerializer.Serialize(accounts, new JsonSerializerOptions { WriteIndented = true }));
}
var logins = new List<LoginResponseDto>();
foreach (var account in accounts)
{
    using var response = await anonymous.PostAsJsonAsync("api/v1/auth/login", new LoginRequestDto(account.Username, account.Password));
    response.EnsureSuccessStatusCode(); logins.Add((await response.Content.ReadFromJsonAsync<LoginResponseDto>())!);
    var profileDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IFBConnect", "profiles", account.Profile);
    Directory.CreateDirectory(profileDirectory);
    await File.WriteAllTextAsync(Path.Combine(profileDirectory, "preferences.json"), JsonSerializer.Serialize(new { Theme = "Light", Server = server.ToString(), Notifications = true, Sounds = true, CloseToTray = false }));
    Console.WriteLine("Created and authenticated employee: " + account.Username + " (" + account.Profile + ")");
}
HttpClient Authenticated(string token) { var client = new HttpClient { BaseAddress = server }; client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token); return client; }
using var aliceClient = Authenticated(logins[0].AccessToken);
using var dmResponse = await aliceClient.PostAsJsonAsync("api/v1/conversations/dm", new { otherUserId = logins[1].User.Id });
dmResponse.EnsureSuccessStatusCode(); var dm = (await dmResponse.Content.ReadFromJsonAsync<ConversationDto>())!;
HubConnection Hub(string token) => new HubConnectionBuilder().WithUrl(new Uri(server, "hubs/chat"), options => options.AccessTokenProvider = () => Task.FromResult<string?>(token)).Build();
await using var aliceHub = Hub(logins[0].AccessToken); await using var bobHub = Hub(logins[1].AccessToken);
var receivedByBob = new TaskCompletionSource<MessageDto>(TaskCreationOptions.RunContinuationsAsynchronously);
var receivedByAlice = new TaskCompletionSource<MessageDto>(TaskCreationOptions.RunContinuationsAsynchronously);
bobHub.On<MessageDto>(nameof(IClientHub.MessageReceivedAsync), m => { if (m.Content == "QA: Hello Bob — live two-session check") receivedByBob.TrySetResult(m); });
aliceHub.On<MessageDto>(nameof(IClientHub.MessageReceivedAsync), m => { if (m.Content == "QA: Hello Alice — reply confirmed") receivedByAlice.TrySetResult(m); });
await aliceHub.StartAsync(); await bobHub.StartAsync();
await aliceHub.InvokeAsync(nameof(IServerHub.SendMessageAsync), new SendMessageDto(dm.Id, "QA: Hello Bob — live two-session check", MessageType.Text, null, null));
var first = await receivedByBob.Task.WaitAsync(TimeSpan.FromSeconds(10));
await bobHub.InvokeAsync(nameof(IServerHub.SendMessageAsync), new SendMessageDto(dm.Id, "QA: Hello Alice — reply confirmed", MessageType.Text, first.Id, null));
var reply = await receivedByAlice.Task.WaitAsync(TimeSpan.FromSeconds(10));
if (reply.ReplyToId != first.Id) throw new Exception("Live reply association failed.");
var reaction = new TaskCompletionSource<ReactionUpdatedDto>(TaskCreationOptions.RunContinuationsAsynchronously);
aliceHub.On<ReactionUpdatedDto>(nameof(IClientHub.ReactionUpdatedAsync), r => { if (r.MessageId == first.Id && r.Added) reaction.TrySetResult(r); });
await bobHub.InvokeAsync(nameof(IServerHub.ToggleReactionAsync), new ToggleReactionDto(first.Id, 0));
await reaction.Task.WaitAsync(TimeSpan.FromSeconds(10));
using var bobClient = Authenticated(logins[1].AccessToken);
var png = await File.ReadAllBytesAsync(Path.GetFullPath(Argument("--image")));
using var imageContent = new MultipartFormDataContent();
imageContent.Add(new StringContent(dm.Id), "conversationId");
imageContent.Add(new ByteArrayContent(png), "file", "qa-preview.png");
using var upload = await bobClient.PostAsync("api/v1/files/upload", imageContent);
upload.EnsureSuccessStatusCode();
var uploaded = await upload.Content.ReadFromJsonAsync<JsonElement>();
var fileId = uploaded.GetProperty("id").GetString();
using var download = await aliceClient.GetAsync($"api/v1/files/{fileId}");
download.EnsureSuccessStatusCode();
if (download.Content.Headers.ContentType?.MediaType != "image/png" || !(await download.Content.ReadAsByteArrayAsync()).SequenceEqual(png))
    throw new Exception("Live image download failed verification.");
var imageReceived = new TaskCompletionSource<MessageDto>(TaskCreationOptions.RunContinuationsAsynchronously);
aliceHub.On<MessageDto>(nameof(IClientHub.MessageReceivedAsync), m => { if (m.FileId == fileId) imageReceived.TrySetResult(m); });
await bobHub.InvokeAsync(nameof(IServerHub.SendMessageAsync), new SendMessageDto(dm.Id, "qa-preview.png", MessageType.File, null, fileId));
await imageReceived.Task.WaitAsync(TimeSpan.FromSeconds(10));
var result = new { testedAtUtc = DateTime.UtcNow, server = server.ToString(), accounts = accounts.Select(a => new { a.Username, a.Profile }), conversationId = dm.Id,
    passed = new[] { "Two local employee accounts authenticate", "Two simultaneous authenticated SignalR sessions connect", "Alice-to-Bob delivery", "Bob-to-Alice reply delivery and association", "Reaction broadcasts on the live server", "Image upload and verified PNG download", "Image attachment broadcasts to Alice" } };
await File.WriteAllTextAsync(Path.Combine(output, "live-results.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
await File.WriteAllTextAsync(Path.Combine(output, "TEST-ACCOUNTS.md"), "# Local acceptance accounts\n\nServer: " + server + "\n\n" +
    string.Join("\n", accounts.Select(a => $"- **{a.Username}** — password `{a.Password}`, profile `{a.Profile}`")) +
    "\n\nThese are temporary employee accounts, created for the requested development test. Disable them after acceptance.\n");
Console.WriteLine("PASS: Live bidirectional messaging, reply association and reaction broadcast.");
Console.WriteLine("Account instructions saved to " + Path.Combine(output, "TEST-ACCOUNTS.md"));
record TestAccount(string Id, string Username, string Password, string Profile);

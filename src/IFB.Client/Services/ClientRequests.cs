using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

namespace IFB.Client.Services;
public record SharedFile(string Id, string FileName, string ContentType, long SizeBytes, string Sha256Hash, string? UploadedByName, DateTime UploadedAt);
public record ChannelInfo(string Id, string Name, string? Description, bool Joined);
public sealed class ClientRequests(IHttpClientFactory factory)
{
    public async Task<T> GetAsync<T>(string path)
    { using var client = factory.CreateClient("IFBServer"); return (await client.GetFromJsonAsync<T>(path))!; }
    public async Task<T?> SendAsync<T>(HttpMethod method, string path, object? body = null)
    {
        using var client = factory.CreateClient("IFBServer"); using var request = new HttpRequestMessage(method, path);
        if (body != null) request.Content = JsonContent.Create(body);
        using var response = await client.SendAsync(request); response.EnsureSuccessStatusCode();
        if (response.StatusCode == System.Net.HttpStatusCode.NoContent) return default;
        return await response.Content.ReadFromJsonAsync<T>();
    }
    public Task SendAsync(HttpMethod method, string path, object? body = null) => SendAsync<JsonElement>(method, path, body);
    public async Task<SharedFile> UploadAsync(string conversationId, string path)
    {
        using var client = factory.CreateClient("IFBServer"); client.Timeout = TimeSpan.FromMinutes(3);
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(conversationId), "conversationId");
        form.Add(new StreamContent(File.OpenRead(path)), "file", Path.GetFileName(path));
        using var response = await client.PostAsync("api/v1/files/upload", form); response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SharedFile>())!;
    }
    public async Task DownloadAsync(SharedFile file, string path)
    {
        using var client = factory.CreateClient("IFBServer"); client.Timeout = TimeSpan.FromMinutes(3);
        using var response = await client.GetAsync($"api/v1/files/{file.Id}", HttpCompletionOption.ResponseHeadersRead); response.EnsureSuccessStatusCode();
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".download";
        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var output = File.Create(temporary))
            await using (var input = await response.Content.ReadAsStreamAsync())
            {
                var buffer = new byte[81920]; long total = 0; int count;
                while ((count = await input.ReadAsync(buffer)) > 0)
                { total += count; if (total > file.SizeBytes) throw new IOException("File size verification failed."); hash.AppendData(buffer, 0, count); await output.WriteAsync(buffer.AsMemory(0, count)); }
                if (total != file.SizeBytes || !Convert.ToHexString(hash.GetHashAndReset()).Equals(file.Sha256Hash, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("File integrity verification failed.");
            }
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

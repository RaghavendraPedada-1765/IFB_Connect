using Microsoft.Extensions.Configuration;
using System.Net;
using System.Net.Sockets;

namespace IFB.Client.Services;

public sealed class ServerEndpoint
{
    public Uri Address { get; private set; }
    private readonly ClientPreferences _preferences;
    public ServerEndpoint(IConfiguration config, ClientPreferences preferences)
    { _preferences = preferences; Address = new Uri(preferences.Server ?? $"https://{config["Server:Host"] ?? "localhost"}:{config["Server:Port"] ?? "5001"}/"); }
    public void SetAddress(string address)
    {
        if (!Uri.TryCreate(address.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            !string.IsNullOrEmpty(uri.UserInfo) || uri.AbsolutePath != "/" || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new ArgumentException("Enter an HTTPS server address, for example https://192.168.1.10:5001.");
        Address = uri;
        _preferences.Server = uri.ToString(); _preferences.Save();
    }
    public static HttpMessageHandler CreateHandler() => new SocketsHttpHandler
    {
        UseProxy = false,
        Credentials = CredentialCache.DefaultNetworkCredentials,
        ConnectCallback = async (context, ct) =>
        {
            var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, ct);
            foreach (var address in addresses.Where(IsPrivate))
            {
                var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                try { await socket.ConnectAsync(address, context.DnsEndPoint.Port, ct); return new NetworkStream(socket, true); }
                catch { socket.Dispose(); }
            }
            throw new HttpRequestException("The server must be reachable on a private company network address.");
        }
    };
    private static bool IsPrivate(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (IPAddress.IsLoopback(ip)) return true;
        var b = ip.GetAddressBytes();
        return b.Length == 4 && (b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168));
    }
}

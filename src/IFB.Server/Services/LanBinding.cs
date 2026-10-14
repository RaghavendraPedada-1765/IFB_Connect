using System.Net;
using System.Net.NetworkInformation;
using System.Security.Cryptography.X509Certificates;

namespace IFB.Server.Services;

public static class LanBinding
{
    public static void Configure(WebApplicationBuilder builder)
    {
        var config = builder.Configuration;
        if (!IPAddress.TryParse(config["Server:BindAddress"] ?? "127.0.0.1", out var address) || !IsPrivate(address))
            throw new InvalidOperationException("Server:BindAddress must name a private internal NIC address or loopback. Wildcard/public bindings are prohibited.");
        if (!IPAddress.IsLoopback(address) && !NetworkInterface.GetAllNetworkInterfaces().SelectMany(n => n.GetIPProperties().UnicastAddresses).Any(a => a.Address.Equals(address)))
            throw new InvalidOperationException("Server:BindAddress is not assigned to an interface on this machine.");
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Limits.MaxRequestBodySize = config.GetValue<long>("FileStorage:MaxFileSizeBytes", 52_428_800) + 1_048_576;
            options.Listen(address, config.GetValue("Server:Port", 5001), listen =>
            {
                var certificatePath = config["Server:CertificatePath"];
                if (!string.IsNullOrWhiteSpace(certificatePath)) listen.UseHttps(certificatePath, config["Server:CertificatePassword"]);
                else if (builder.Environment.IsDevelopment()) listen.UseHttps();
                else
                {
                    using var store = new X509Store(StoreName.My, StoreLocation.LocalMachine); store.Open(OpenFlags.ReadOnly);
                    var subject = config["Server:CertificateSubject"] ?? "ifbserver.ifb.local";
                    var cert = store.Certificates.Find(X509FindType.FindBySubjectName, subject, true).Cast<X509Certificate2>()
                        .FirstOrDefault(c => c.HasPrivateKey) ?? throw new InvalidOperationException("Install an internal CA server certificate with a private key, or configure Server:CertificatePath.");
                    listen.UseHttps(cert);
                }
            });
        });
    }
    public static bool IsPrivate(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (IPAddress.IsLoopback(ip)) return true;
        var b = ip.GetAddressBytes();
        return b.Length == 4 && (b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168));
    }
}

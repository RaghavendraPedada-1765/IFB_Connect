using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Options;

namespace IFB.Server.Middleware;

/// <summary>
/// Rejects any inbound connection whose remote IP falls outside the configured
/// private-network CIDR ranges. This is the first middleware in the pipeline.
/// </summary>
public sealed class LanGuardMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<LanGuardMiddleware> _logger;
    private readonly List<(IPAddress Network, IPAddress Mask)> _allowedRanges;
    private readonly bool _rejectOutside;

    public LanGuardMiddleware(
        RequestDelegate next,
        IConfiguration config,
        ILogger<LanGuardMiddleware> logger)
    {
        _next = next;
        _logger = logger;
        _rejectOutside = bool.Parse(config["LanSecurity:RejectOutsideRanges"] ?? "true");

        _allowedRanges = new();
        var cidrs = config.GetSection("LanSecurity:AllowedCidrRanges").Get<string[]>()
                    ?? ["10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16", "127.0.0.1/32"];

        foreach (var cidr in cidrs)
        {
            var parts = cidr.Split('/');
            var network = IPAddress.Parse(parts[0]);
            int prefixLen = int.Parse(parts[1]);
            var mask = PrefixLengthToMask(network.AddressFamily, prefixLen);
            _allowedRanges.Add((network, mask));
        }
    }

    public async Task InvokeAsync(HttpContext ctx)
    {
        if (!_rejectOutside)
        {
            await _next(ctx);
            return;
        }

        var remoteIp = ctx.Connection.RemoteIpAddress;
        if (remoteIp == null || !IsAllowed(remoteIp))
        {
            _logger.LogWarning(
                "LAN Guard: rejected connection from {IP} — outside allowed ranges",
                remoteIp?.ToString() ?? "unknown");

            ctx.Response.StatusCode = 403;
            await ctx.Response.WriteAsync("Access restricted to company network.");
            return;
        }

        await _next(ctx);
    }

    private bool IsAllowed(IPAddress ip)
    {
        // Normalize IPv4-mapped IPv6 (::ffff:192.168.x.x)
        if (ip.IsIPv4MappedToIPv6)
            ip = ip.MapToIPv4();

        foreach (var (network, mask) in _allowedRanges)
        {
            if (network.AddressFamily != ip.AddressFamily) continue;

            var ipBytes = ip.GetAddressBytes();
            var netBytes = network.GetAddressBytes();
            var maskBytes = mask.GetAddressBytes();

            bool match = true;
            for (int i = 0; i < ipBytes.Length; i++)
            {
                if ((ipBytes[i] & maskBytes[i]) != (netBytes[i] & maskBytes[i]))
                {
                    match = false;
                    break;
                }
            }
            if (match) return true;
        }
        return false;
    }

    private static IPAddress PrefixLengthToMask(AddressFamily family, int prefixLength)
    {
        int byteCount = family == AddressFamily.InterNetworkV6 ? 16 : 4;
        if (prefixLength < 0 || prefixLength > byteCount * 8) throw new ArgumentException("Invalid LAN CIDR prefix length.");
        byte[] mask = new byte[byteCount];
        for (int i = 0; i < prefixLength; i++)
            mask[i / 8] |= (byte)(0x80 >> (i % 8));
        return new IPAddress(mask);
    }
}

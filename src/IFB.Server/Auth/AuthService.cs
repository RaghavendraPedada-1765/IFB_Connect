using IFB.Server.Data;
using IFB.Server.Models;
using IFB.Shared.DTOs;
using IFB.Shared.Enums;
using Konscious.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.DirectoryServices.AccountManagement;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace IFB.Server.Auth;

public interface IAuthService
{
    Task<LoginResponseDto?> LoginAsync(LoginRequestDto dto, string ipAddress);
    Task<LoginResponseDto?> WindowsLoginAsync(string identityName);
    Task<LoginResponseDto?> RefreshAsync(string refreshToken, string ipAddress);
    Task RevokeRefreshTokenAsync(string token);
    Task<UserDto?> GetCurrentUserAsync(string userId);
}

public sealed class AuthService : IAuthService
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;
    private readonly ILogger<AuthService> _logger;
    private readonly RSA _rsa;

    public AuthService(AppDbContext db, IConfiguration config, ILogger<AuthService> logger, JwtKeyStore keys)
    {
        _db = db;
        _config = config;
        _logger = logger;

        _rsa = keys.SigningKey;
    }

    public async Task<LoginResponseDto?> LoginAsync(LoginRequestDto dto, string ipAddress)
    {
        if (string.IsNullOrWhiteSpace(dto.Username) || string.IsNullOrWhiteSpace(dto.Password))
            return null;

        // Sanitize — prevent injection into LDAP or SQL
        var username = SanitizeUsername(dto.Username);

        User? user = null;
        var authMode = _config["Auth:Mode"] ?? "ActiveDirectory";

        // ── Active Directory auth ────────────────────────────────────────────
        if (authMode == "ActiveDirectory")
        {
            try
            {
                var domain = _config["Auth:ActiveDirectory:Domain"] ?? "IFB.LOCAL";
                using var ctx = new PrincipalContext(ContextType.Domain, domain);
                bool valid = ctx.ValidateCredentials(username, dto.Password,
                    ContextOptions.Negotiate | ContextOptions.Signing | ContextOptions.Sealing);

                if (!valid)
                {
                    _logger.LogWarning("AD auth failed for {User}", username);
                    return null;
                }

                // Sync or create local shadow record
                user = await SyncAdUserAsync(username, ctx);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AD auth error for {User}. Falling back to local.", username);
                // Fall through to local auth
            }
        }

        // ── Local account fallback ───────────────────────────────────────────
        if (user == null)
        {
            var localEnabled = bool.Parse(_config["Auth:LocalFallback:Enabled"] ?? "true");
            if (!localEnabled) return null;

            var localUser = await _db.Users
                .FirstOrDefaultAsync(u => u.Username == username && u.IsLocalAccount && u.IsActive);

            if (localUser == null) return null;
            if (!VerifyPassword(dto.Password, localUser.PasswordHash!, localUser.PasswordSalt!)) return null;
            user = localUser;
        }

        if (user == null || !user.IsActive) return null;

        user.LastLoginAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return await GenerateTokensAsync(user);
    }

    public async Task<LoginResponseDto?> WindowsLoginAsync(string identityName)
    {
        if (!_config.GetValue("Auth:WindowsIntegrated:Enabled", false)) return null;
        var username = SanitizeUsername(identityName);
        using var context = new PrincipalContext(ContextType.Domain, _config["Auth:ActiveDirectory:Domain"] ?? "IFB.LOCAL");
        using var principal = UserPrincipal.FindByIdentity(context, username);
        if (principal == null) return null;
        var user = await SyncAdUserAsync(username, context);
        if (!user.IsActive) return null;
        user.LastLoginAt = DateTime.UtcNow; await _db.SaveChangesAsync(); return await GenerateTokensAsync(user);
    }

    public async Task<LoginResponseDto?> RefreshAsync(string refreshToken, string ipAddress)
    {
        var tokenHash = HashToken(refreshToken);
        var token = await _db.RefreshTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.Token == tokenHash && !t.IsRevoked);

        if (token == null || token.ExpiresAt < DateTime.UtcNow)
            return null;

        var user = token.User ?? await _db.Users.FindAsync(token.UserId);
        if (user == null || !user.IsActive) return null;

        // Rotate token
        // Atomically consume the old token so concurrent refreshes cannot both succeed.
        var consumed = await _db.RefreshTokens.Where(t => t.Id == token.Id && !t.IsRevoked)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.IsRevoked, true));
        if (consumed != 1) return null;
        token.IsRevoked = true;
        var newResponse = await GenerateTokensAsync(user);
        token.ReplacedByToken = HashToken(newResponse.RefreshToken);
        await _db.SaveChangesAsync();

        return newResponse;
    }

    public async Task RevokeRefreshTokenAsync(string token)
    {
        var hash = HashToken(token);
        var entity = await _db.RefreshTokens.FirstOrDefaultAsync(t => t.Token == hash);
        if (entity != null)
        {
            entity.IsRevoked = true;
            await _db.SaveChangesAsync();
        }
    }

    public async Task<UserDto?> GetCurrentUserAsync(string userId)
    {
        var user = await _db.Users.FindAsync(userId);
        return user == null ? null : MapToDto(user);
    }

    // ── Private helpers ─────────────────────────────────────────────────────

    private async Task<LoginResponseDto> GenerateTokensAsync(User user)
    {
        var expiryMinutes = int.Parse(_config["Jwt:AccessTokenExpiryMinutes"] ?? "480");
        var expiry = DateTime.UtcNow.AddMinutes(expiryMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new(JwtRegisteredClaimNames.UniqueName, user.Username),
            new(JwtRegisteredClaimNames.Name, user.DisplayName),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(ClaimTypes.Role, user.Role.ToString()),
            new("dept", user.Department ?? ""),
            new("token_version", user.TokenVersion.ToString()),
        };

        var signingCreds = new SigningCredentials(
            new RsaSecurityKey(_rsa), SecurityAlgorithms.RsaSha256);

        var jwt = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"],
            audience: _config["Jwt:Audience"],
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expiry,
            signingCredentials: signingCreds);

        var accessToken = new JwtSecurityTokenHandler().WriteToken(jwt);

        // Refresh token — cryptographically random, stored hashed
        var refreshBytes = RandomNumberGenerator.GetBytes(64);
        var refreshToken = Convert.ToBase64String(refreshBytes);
        var refreshExpiry = DateTime.UtcNow.AddDays(
            int.Parse(_config["Jwt:RefreshTokenExpiryDays"] ?? "7"));

        _db.RefreshTokens.Add(new RefreshToken
        {
            Token = HashToken(refreshToken),
            UserId = user.Id,
            ExpiresAt = refreshExpiry
        });
        await _db.SaveChangesAsync();

        return new LoginResponseDto(accessToken, refreshToken, expiry, MapToDto(user));
    }

    private async Task<User> SyncAdUserAsync(string username, PrincipalContext ctx)
    {
        using var principal = UserPrincipal.FindByIdentity(ctx, username);

        var existing = await _db.Users.FirstOrDefaultAsync(u => u.Username == username);
        if (existing != null)
        {
            if (existing.IsLocalAccount) throw new InvalidOperationException("AD identity conflicts with a local account. Use a distinct local username.");
            existing.DisplayName = principal?.DisplayName ?? username;
            existing.Email = principal?.EmailAddress ?? existing.Email;
            await _db.SaveChangesAsync();
            return existing;
        }

        var newUser = new User
        {
            Id = Guid.NewGuid().ToString(),
            Username = username,
            DisplayName = principal?.DisplayName ?? username,
            Email = principal?.EmailAddress ?? $"{username}@ifb.local",
            IsLocalAccount = false,
            Role = UserRole.Employee,
            CreatedAt = DateTime.UtcNow
        };

        _db.Users.Add(newUser);
        await _db.SaveChangesAsync();
        return newUser;
    }

    private static bool VerifyPassword(string password, string hash, string salt)
    {
        var saltBytes = Convert.FromBase64String(salt);
        using var argon2 = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = saltBytes,
            DegreeOfParallelism = 2,
            Iterations = 4,
            MemorySize = 65536
        };
        var computed = Convert.ToBase64String(argon2.GetBytes(64));
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(computed),
            Encoding.UTF8.GetBytes(hash));
    }

    public static (string Hash, string Salt) HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(32);
        using var argon2 = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            DegreeOfParallelism = 2,
            Iterations = 4,
            MemorySize = 65536
        };
        return (Convert.ToBase64String(argon2.GetBytes(64)), Convert.ToBase64String(salt));
    }

    private static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static string SanitizeUsername(string username)
    {
        if (username.Contains('\\')) username = username.Split('\\')[^1];
        // Remove LDAP special characters and null bytes
        return new string(username
            .Replace("\0", "")
            .Replace("(", "")
            .Replace(")", "")
            .Replace("*", "")
            .Replace("\\", "")
            .Where(c => !char.IsControl(c))
            .ToArray())
            .Trim()
            .ToLowerInvariant();
    }

    private static UserDto MapToDto(User u) => new(
        u.Id, u.DisplayName, u.Email,
        u.Title, u.Department, u.AvatarPath,
        u.Role, u.Presence);
}

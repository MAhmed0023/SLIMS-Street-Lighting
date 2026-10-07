using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.Tokens;
using Slims.Application.Interfaces;
using Slims.Domain.Entities;

namespace Slims.Infrastructure.Identity;

/// <summary>PBKDF2-SHA256, 120 000 iterations, 16-byte salt, 32-byte hash (base64).</summary>
public sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    public const int Iterations = 120_000;
    public (string Hash, string Salt) Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, Iterations, HashAlgorithmName.SHA256, 32);
        return (Convert.ToBase64String(hash), Convert.ToBase64String(salt));
    }
    public bool Verify(string password, string? hash, string? salt)
    {
        if (string.IsNullOrEmpty(hash) || string.IsNullOrEmpty(salt)) return false;
        var computed = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), Convert.FromBase64String(salt), Iterations, HashAlgorithmName.SHA256, 32);
        var expected = Convert.FromBase64String(hash);
        return computed.Length == expected.Length && CryptographicOperations.FixedTimeEquals(computed, expected);
    }
}

public sealed class JwtOptions
{
    public string Issuer { get; set; } = "Slims";
    public string Audience { get; set; } = "Slims";
    public string Key { get; set; } = "";
    public int AccessMinutes { get; set; } = 60;
    public int RefreshDays { get; set; } = 7;
}

/// <summary>Signed JWT access tokens and opaque refresh tokens (only their SHA-256 hash is stored).</summary>
public sealed class JwtTokenService(JwtOptions options) : ITokenService
{
    private readonly SymmetricSecurityKey _key = new(Encoding.UTF8.GetBytes(options.Key));

    public (string Token, DateTime ExpiresAt) CreateAccessToken(User user, Guid? deviceId = null)
    {
        var expires = DateTime.UtcNow.AddMinutes(options.AccessMinutes);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id), new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new(ClaimTypes.NameIdentifier, user.Id), new(ClaimTypes.Name, user.Name), new(ClaimTypes.Role, user.RoleKey),
        };
        if (deviceId is not null) claims.Add(new("dev", deviceId.Value.ToString()));
        var token = new JwtSecurityToken(options.Issuer, options.Audience, claims, notBefore: DateTime.UtcNow, expires: expires, signingCredentials: new SigningCredentials(_key, SecurityAlgorithms.HmacSha256));
        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }

    public (string Token, DateTime ExpiresAt) CreateRefreshToken()
        => (Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)).TrimEnd('=').Replace('+', '-').Replace('/', '_'), DateTime.UtcNow.AddDays(options.RefreshDays));

    public string HashToken(string token) => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}

/// <summary>The caller, from the validated bearer token and request headers.</summary>
public sealed class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private HttpContext? Ctx => accessor.HttpContext;
    public string? UserId => Ctx?.User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? Ctx?.User?.FindFirstValue(JwtRegisteredClaimNames.Sub);
    public string? UserAgent => Ctx?.Request.Headers.UserAgent.ToString();
    public string? Ip => Ctx?.Request.Headers["X-Forwarded-For"].FirstOrDefault()?.Split(',')[0].Trim() ?? Ctx?.Connection.RemoteIpAddress?.ToString();
    public Guid? DeviceId => Guid.TryParse(Ctx?.User?.FindFirstValue("dev"), out var g) ? g : null;
}

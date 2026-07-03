using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using SiaWebhookApi.Configuration;

namespace SiaWebhookApi.Services;

/// <summary>
/// Verifies the admin password and issues signed JWTs for the settings API.
/// </summary>
public class AuthService
{
    private readonly AuthOptions _options;

    public AuthService(AuthOptions options) => _options = options;

    /// <summary>
    /// Constant-time comparison of the submitted password against the configured one.
    /// Both sides are SHA-256 hashed first so the comparison length is fixed —
    /// this avoids leaking the password length and the early-exit timing of a naive
    /// string compare (a classic timing side-channel).
    /// </summary>
    public bool VerifyPassword(string? submitted)
    {
        if (string.IsNullOrEmpty(submitted))
            return false;

        Span<byte> a = stackalloc byte[32];
        Span<byte> b = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(submitted), a);
        SHA256.HashData(Encoding.UTF8.GetBytes(_options.AdminPassword), b);
        return CryptographicOperations.FixedTimeEquals(a, b);
    }

    /// <summary>
    /// Issues an HS256-signed JWT for the admin, valid for TokenLifetimeHours.
    /// </summary>
    public (string token, DateTime expiresUtc) GenerateToken()
    {
        var now = DateTime.UtcNow;
        var expires = now.AddHours(_options.TokenLifetimeHours);

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = expires,
            Subject = new ClaimsIdentity(new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, "admin"),
                new Claim(ClaimTypes.Role, "admin"),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            }),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.JwtSecret)),
                SecurityAlgorithms.HmacSha256)
        };

        var token = new JsonWebTokenHandler().CreateToken(descriptor);
        return (token, expires);
    }
}

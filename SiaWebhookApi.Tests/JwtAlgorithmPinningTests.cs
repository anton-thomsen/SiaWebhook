using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.IdentityModel.Tokens;
using SiaWebhookApi.Configuration;

namespace SiaWebhookApi.Tests;

/// <summary>
/// The API must accept only HS256-signed JWTs. A token signed with any other
/// algorithm — even with the correct key and claims — must be rejected,
/// closing algorithm-confusion/downgrade attacks against the settings API.
/// </summary>
public class JwtAlgorithmPinningTests : IDisposable
{
    // 64 chars = 512 bits, large enough to sign with every HMAC variant under test.
    private const string JwtSecret = "pinning-test-secret-64-chars-long-0123456789ABCDEF0123456789ABC!";

    private readonly WebApplicationFactory<Program> _factory;
    private readonly string? _savedSecret;

    public JwtAlgorithmPinningTests()
    {
        _savedSecret = Environment.GetEnvironmentVariable("SIA_JWT_SECRET");
        Environment.SetEnvironmentVariable("SIA_JWT_SECRET", JwtSecret);
        _factory = new WebApplicationFactory<Program>();
    }

    public void Dispose()
    {
        _factory.Dispose();
        Environment.SetEnvironmentVariable("SIA_JWT_SECRET", _savedSecret);
    }

    private static string MakeToken(string algorithm)
    {
        // Same key, same claims as a legitimate token — only the algorithm differs.
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtSecret));
        var auth = new AuthOptions();
        var token = new JwtSecurityToken(
            issuer: auth.Issuer,
            audience: auth.Audience,
            claims: [new Claim(ClaimTypes.Name, "admin")],
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: DateTime.UtcNow.AddMinutes(10),
            signingCredentials: new SigningCredentials(key, algorithm));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private async Task<HttpStatusCode> GetSettingsWith(string token)
    {
        using var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/settings");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.SendAsync(request);
        return response.StatusCode;
    }

    [Fact]
    public async Task Hs256Token_IsAccepted()
    {
        var status = await GetSettingsWith(MakeToken(SecurityAlgorithms.HmacSha256));

        Assert.Equal(HttpStatusCode.OK, status);
    }

    [Theory]
    [InlineData(SecurityAlgorithms.HmacSha384)]
    [InlineData(SecurityAlgorithms.HmacSha512)]
    public async Task NonHs256Token_IsRejected_EvenWithCorrectKeyAndClaims(string algorithm)
    {
        var status = await GetSettingsWith(MakeToken(algorithm));

        Assert.Equal(HttpStatusCode.Unauthorized, status);
    }
}

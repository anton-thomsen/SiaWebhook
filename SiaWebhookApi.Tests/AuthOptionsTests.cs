using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SiaWebhookApi.Configuration;

namespace SiaWebhookApi.Tests;

/// <summary>
/// Production must refuse to start with a weak or default admin password —
/// the admin panel controls the alarm pipeline, so a guessable password is
/// a deployment error, not a warning.
/// </summary>
public class AuthOptionsTests : IDisposable
{
    private static readonly string[] SecretVars = ["SIA_JWT_SECRET", "SIA_ADMIN_PASSWORD", "ZEPCAM_API_KEY"];

    private readonly Dictionary<string, string?> _savedEnv = [];

    public AuthOptionsTests()
    {
        foreach (var name in SecretVars)
            _savedEnv[name] = Environment.GetEnvironmentVariable(name);

        Environment.SetEnvironmentVariable("SIA_JWT_SECRET", "auth-options-test-secret-0123456789ABCDEF-XYZ");
        Environment.SetEnvironmentVariable("ZEPCAM_API_KEY", "auth-options-test-key");
    }

    public void Dispose()
    {
        foreach (var (name, value) in _savedEnv)
            Environment.SetEnvironmentVariable(name, value);
    }

    private static AuthOptions Load(bool isProduction) =>
        AuthOptions.Load(
            new ConfigurationBuilder().Build(),
            NullLogger.Instance,
            isProduction);

    [Theory]
    [InlineData("short1!A")]                 // too short
    [InlineData("alllowercase123!!")]        // no uppercase
    [InlineData("ALLUPPERCASE123!!")]        // no lowercase
    [InlineData("NoDigitsHere!!!!")]         // no digit
    [InlineData("NoSymbolsHere1234")]        // no symbol
    public void Production_WeakAdminPassword_FailsStartup(string password)
    {
        Environment.SetEnvironmentVariable("SIA_ADMIN_PASSWORD", password);

        Assert.Throws<InvalidOperationException>(() => Load(isProduction: true));
    }

    [Fact]
    public void Production_StrongAdminPassword_Loads()
    {
        Environment.SetEnvironmentVariable("SIA_ADMIN_PASSWORD", "Str0ng&Secure-Passw0rd");

        var options = Load(isProduction: true);

        Assert.Equal("Str0ng&Secure-Passw0rd", options.AdminPassword);
    }

    [Fact]
    public void Development_WeakAdminPassword_LoadsWithWarningOnly()
    {
        Environment.SetEnvironmentVariable("SIA_ADMIN_PASSWORD", "admin");

        var options = Load(isProduction: false);

        Assert.Equal("admin", options.AdminPassword);
    }
}

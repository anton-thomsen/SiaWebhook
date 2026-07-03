using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SiaWebhookApi.Configuration;
using SiaWebhookApi.Services;

namespace SiaWebhookApi.Tests;

/// <summary>
/// Blocker 5: GET /api/settings must never return the AES encryption key.
/// Blocker 4: receiver IP/port are env-managed — a settings update must not
/// be able to divert alarms to a different endpoint.
/// </summary>
public class SiaSettingsServiceTests : IDisposable
{
    private const string ConfiguredKey = "0123456789abcdef0123456789abcdef";

    private static readonly string[] GuardedEnvVars =
        ["SIA_ENCRYPTION_KEY", "SIA_RECEIVER_IP", "SIA_RECEIVER_PORT"];

    private readonly string _tempDir;
    private readonly Dictionary<string, string?> _savedEnv = [];

    public SiaSettingsServiceTests()
    {
        // Tests control these env vars explicitly; save and restore the
        // machine's values so the suite is environment-independent.
        foreach (var name in GuardedEnvVars)
        {
            _savedEnv[name] = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, null);
        }

        _tempDir = Path.Combine(Path.GetTempPath(), $"sia-settings-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(Path.Combine(_tempDir, "appsettings.json"), "{}");
    }

    public void Dispose()
    {
        foreach (var (name, value) in _savedEnv)
            Environment.SetEnvironmentVariable(name, value);
        Directory.Delete(_tempDir, recursive: true);
    }

    private SiaSettingsService BuildService(string? encryptionKey = null)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SiaSettings:IpAddress"]      = "10.0.0.1",
                ["SiaSettings:Port"]           = "55001",
                ["SiaSettings:EncryptionKey"]  = encryptionKey,
                ["SiaSettings:AccountNumber"]  = "A001",
                ["SiaSettings:ReceiverNumber"] = "R0",
                ["SiaSettings:AccountPrefix"]  = "L0",
                ["SiaSettings:EventCode"]      = "NPA"
            })
            .Build();

        var env = new Mock<IWebHostEnvironment>();
        env.SetupGet(e => e.ContentRootPath).Returns(_tempDir);

        return new SiaSettingsService(
            env.Object,
            NullLogger<SiaSettingsService>.Instance,
            config,
            Mock.Of<ISiaTransmitter>(),
            new SiaRuntimeSettingsOptions { FilePath = Path.Combine(_tempDir, "settings.json") });
    }

    private static SiaSettingsUpdateRequest ValidUpdate() => new()
    {
        AccountNumber  = "A002",
        ReceiverNumber = "R1",
        AccountPrefix  = "L1",
        EventCode      = "NPA"
    };

    [Fact]
    public async Task UpdateAsync_PersistedSettings_SurviveRestart()
    {
        var (success, _) = await BuildService().UpdateAsync(ValidUpdate());
        Assert.True(success);

        // A fresh service instance (same settings file) simulates a container
        // restart: the UI-edited identity fields must be overlaid from disk.
        var restarted = BuildService();

        Assert.Equal("A002", restarted.Get().AccountNumber);
        Assert.Equal("R1", restarted.Get().ReceiverNumber);
    }

    // ── Blocker 5: no key exposure ────────────────────────────────────

    [Fact]
    public void GetPublic_WithConfiguredKey_ReportsEnabledWithoutExposingKey()
    {
        var response = BuildService(ConfiguredKey).GetPublic();

        Assert.True(response.EncryptionEnabled);

        // The serialized API response must contain no key material at all.
        var json = JsonSerializer.Serialize(response, JsonSerializerOptions.Web);
        Assert.DoesNotContain(ConfiguredKey, json);
        Assert.DoesNotContain("encryptionKey", json);
        Assert.Contains("\"encryptionEnabled\":true", json);
    }

    [Fact]
    public void GetPublic_WithoutAnyKey_ReportsDisabled()
    {
        var response = BuildService(encryptionKey: null).GetPublic();

        Assert.False(response.EncryptionEnabled);
    }

    [Fact]
    public void GetPublic_WithEnvVarKey_ReportsEnabled()
    {
        Environment.SetEnvironmentVariable("SIA_ENCRYPTION_KEY", ConfiguredKey);
        try
        {
            var response = BuildService(encryptionKey: null).GetPublic();

            Assert.True(response.EncryptionEnabled);
            var json = JsonSerializer.Serialize(response, JsonSerializerOptions.Web);
            Assert.DoesNotContain(ConfiguredKey, json);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SIA_ENCRYPTION_KEY", null);
        }
    }

    [Fact]
    public async Task UpdateAsync_PreservesEncryptionKey()
    {
        var svc = BuildService(ConfiguredKey);

        var (success, _) = await svc.UpdateAsync(ValidUpdate());

        Assert.True(success);
        Assert.Equal(ConfiguredKey, svc.Get().EncryptionKey);
        Assert.Equal("A002", svc.Get().AccountNumber);
    }

    // ── Blocker 4: receiver endpoint cannot be diverted ───────────────

    [Fact]
    public async Task UpdateAsync_CannotChangeReceiverEndpoint()
    {
        var svc = BuildService();

        // The request type has no IP/port fields; a full update must leave
        // the configured receiver endpoint untouched.
        var (success, _) = await svc.UpdateAsync(ValidUpdate());

        Assert.True(success);
        Assert.Equal("10.0.0.1", svc.Get().IpAddress);
        Assert.Equal(55001, svc.Get().Port);
    }

    [Fact]
    public void Get_EnvManagedReceiverEndpoint_AlwaysWins()
    {
        Environment.SetEnvironmentVariable("SIA_RECEIVER_IP", "10.99.99.99");
        Environment.SetEnvironmentVariable("SIA_RECEIVER_PORT", "56000");
        try
        {
            var settings = BuildService().Get();

            Assert.Equal("10.99.99.99", settings.IpAddress);
            Assert.Equal(56000, settings.Port);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SIA_RECEIVER_IP", null);
            Environment.SetEnvironmentVariable("SIA_RECEIVER_PORT", null);
        }
    }

    [Theory]
    [InlineData("A[001]")]
    [InlineData("A|001")]
    [InlineData("A_001")]
    [InlineData("A\r001")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task UpdateAsync_ReservedOrMissingIdentityField_IsRejected(string accountNumber)
    {
        var svc = BuildService();

        var update = ValidUpdate();
        update.AccountNumber = accountNumber;
        var (success, error) = await svc.UpdateAsync(update);

        Assert.False(success);
        Assert.NotNull(error);
        // Nothing was applied.
        Assert.Equal("A001", svc.Get().AccountNumber);
    }
}

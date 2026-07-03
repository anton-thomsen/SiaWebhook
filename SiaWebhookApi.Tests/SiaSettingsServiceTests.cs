using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SiaWebhookApi.Configuration;
using SiaWebhookApi.Services;

namespace SiaWebhookApi.Tests;

/// <summary>
/// Blocker 5: GET /api/settings must never return the AES encryption key —
/// only an encryptionEnabled boolean — and a settings save must not be able
/// to set or wipe the key.
/// </summary>
public class SiaSettingsServiceTests : IDisposable
{
    private const string ConfiguredKey = "0123456789abcdef0123456789abcdef";

    private readonly string _tempDir;
    private readonly string? _savedEnvKey;

    public SiaSettingsServiceTests()
    {
        // Tests control SIA_ENCRYPTION_KEY explicitly; save and restore the
        // machine's value so the suite is environment-independent.
        _savedEnvKey = Environment.GetEnvironmentVariable("SIA_ENCRYPTION_KEY");
        Environment.SetEnvironmentVariable("SIA_ENCRYPTION_KEY", null);

        _tempDir = Path.Combine(Path.GetTempPath(), $"sia-settings-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(Path.Combine(_tempDir, "appsettings.json"), "{}");
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("SIA_ENCRYPTION_KEY", _savedEnvKey);
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
            Mock.Of<ISiaTransmitter>());
    }

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
    public async Task UpdateAsync_ClientSuppliedKey_IsIgnored()
    {
        var svc = BuildService(ConfiguredKey);

        var (success, _) = await svc.UpdateAsync(new SiaSettings
        {
            IpAddress      = "10.0.0.1",
            Port           = 55001,
            EncryptionKey  = "attacker-controlled-key-value---",
            AccountNumber  = "A001",
            ReceiverNumber = "R0",
            AccountPrefix  = "L0",
            EventCode      = "NPA"
        });

        Assert.True(success);
        Assert.Equal(ConfiguredKey, svc.Get().EncryptionKey);
    }

    [Fact]
    public async Task UpdateAsync_NullKey_DoesNotWipeStoredKey()
    {
        var svc = BuildService(ConfiguredKey);

        var (success, _) = await svc.UpdateAsync(new SiaSettings
        {
            IpAddress      = "10.0.0.1",
            Port           = 55001,
            EncryptionKey  = null,
            AccountNumber  = "A002",
            ReceiverNumber = "R0",
            AccountPrefix  = "L0",
            EventCode      = "NPA"
        });

        Assert.True(success);
        Assert.Equal(ConfiguredKey, svc.Get().EncryptionKey);
        Assert.Equal("A002", svc.Get().AccountNumber);
    }
}

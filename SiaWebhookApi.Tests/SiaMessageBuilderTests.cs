using SiaWebhookApi.Models;
using SiaWebhookApi.Services;

namespace SiaWebhookApi.Tests;

public class SiaMessageBuilderTests
{
    private static SiaMessage MakeMessage() => new()
    {
        AccountNumber  = "A001",
        ReceiverNumber = "R0",
        AccountPrefix  = "L0",
        EventCode      = "NPA",
        SequenceNumber = "0001",
        Latitude       = "52.3676",
        Longitude      = "4.9041",
        ExtendedData   = "John|U001|ZEPCAM",
        Timestamp      = new DateTimeOffset(2025, 1, 1, 12, 0, 0, TimeSpan.Zero)
    };

    // ── Unencrypted ───────────────────────────────────────────────────

    [Fact]
    public void Build_Unencrypted_IdTokenHasNoAsterisk()
    {
        var payload = new SiaMessageBuilder().Build(MakeMessage());
        Assert.StartsWith("\"SIA-DCS\"", payload);
    }

    [Fact]
    public void Build_Unencrypted_ContainsSequenceAndAccountFields()
    {
        var msg = MakeMessage();
        string payload = new SiaMessageBuilder().Build(msg);
        Assert.Contains(msg.SequenceNumber, payload);
        Assert.Contains(msg.ReceiverNumber, payload);
        Assert.Contains(msg.AccountNumber, payload);
    }

    [Fact]
    public void Build_Unencrypted_DataRegionContainsCoordinatesAndEventCode()
    {
        var msg = MakeMessage();
        string payload = new SiaMessageBuilder().Build(msg);
        Assert.Contains(msg.Latitude, payload);
        Assert.Contains(msg.Longitude, payload);
        Assert.Contains(msg.EventCode, payload);
    }

    [Fact]
    public void Build_Unencrypted_ContainsOpenBracket()
    {
        string payload = new SiaMessageBuilder().Build(MakeMessage());
        Assert.Contains("[", payload);
    }

    // ── Encrypted ─────────────────────────────────────────────────────

    [Fact]
    public void Build_Encrypted_IdTokenHasAsterisk()
    {
        var crypto = new SiaCryptoService(new byte[32]);
        string payload = new SiaMessageBuilder(crypto).Build(MakeMessage());
        Assert.StartsWith("\"*SIA-DCS\"", payload);
    }

    [Fact]
    public void Build_Encrypted_DataRegionIsHexOnly()
    {
        // After the '[' the data region must be all uppercase hex (encrypted bytes)
        var crypto = new SiaCryptoService(new byte[32]);
        string payload = new SiaMessageBuilder(crypto).Build(MakeMessage());

        int bracketIdx = payload.IndexOf('[');
        Assert.True(bracketIdx >= 0, "Encrypted payload must contain '['");

        string afterBracket = payload[(bracketIdx + 1)..];
        Assert.Matches("^[0-9A-F]+$", afterBracket);
    }

    [Fact]
    public void Build_Encrypted_HeaderIsStillPlaintext()
    {
        // The header (before the data region) must never be encrypted per spec 5.4.x
        var crypto = new SiaCryptoService(new byte[32]);
        var msg = MakeMessage();
        string payload = new SiaMessageBuilder(crypto).Build(msg);

        int bracketIdx = payload.IndexOf('[');
        string header = payload[..bracketIdx];

        Assert.Contains(msg.SequenceNumber, header);
        Assert.Contains(msg.AccountNumber, header);
    }
}

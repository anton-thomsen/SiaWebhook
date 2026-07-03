using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SiaWebhookApi.Configuration;
using SiaWebhookApi.Models;
using SiaWebhookApi.Services;

namespace SiaWebhookApi.Tests;

public class SiaAlarmServiceTests
{
    private static SiaSettings MakeSettings(string? encryptionKey = null) => new()
    {
        IpAddress      = "127.0.0.1",
        Port           = 9000,
        AccountNumber  = "A001",
        ReceiverNumber = "R0",
        AccountPrefix  = "L0",
        EventCode      = "NPA",
        EncryptionKey  = encryptionKey
    };

    private static DataTransferObject MakeDto() => new()
    {
        UserName       = "John",
        UserExternalId = "U001",
        Issuer         = "ZEPCAM",
        Location       = new Location { Latitude = "52.3676", Longitude = "4.9041" }
    };

    private static SiaAlarmService BuildService(Mock<ISiaTransmitter> transmitterMock) =>
        new(transmitterMock.Object, new SiaSequenceCounter(), NullLogger<SiaAlarmService>.Instance);

    // ── Happy path ────────────────────────────────────────────────────

    [Fact]
    public async Task SendAlarmAsync_CallsTransmitter_WithCorrectEndpoint()
    {
        var mock = new Mock<ISiaTransmitter>();
        mock.Setup(t => t.SendAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>()))
            .ReturnsAsync("ACK");

        var svc = BuildService(mock);
        await svc.SendAlarmAsync(MakeDto(), MakeSettings());

        mock.Verify(t => t.SendAsync("127.0.0.1", 9000, It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task SendAlarmAsync_MessageIsCorrectlyFramed()
    {
        // The final SIA message must be framed as \n{CRC}{LEN}{payload}\r
        var mock = new Mock<ISiaTransmitter>();
        string? captured = null;
        mock.Setup(t => t.SendAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>()))
            .Callback<string, int, string>((_, _, msg) => captured = msg)
            .ReturnsAsync("ACK");

        await BuildService(mock).SendAlarmAsync(MakeDto(), MakeSettings());

        Assert.NotNull(captured);
        Assert.StartsWith("\n", captured);
        Assert.EndsWith("\r", captured);
        // First 4 chars after \n = 4-char uppercase hex CRC
        Assert.Matches("^\n[0-9A-F]{4}", captured);
        // Next 4 chars = 4-char length field starting with '0'
        Assert.Matches("^\n[0-9A-F]{4}0[0-9A-F]{3}", captured);
    }

    [Fact]
    public async Task SendAlarmAsync_PayloadContainsSiaDcsToken()
    {
        var mock = new Mock<ISiaTransmitter>();
        string? captured = null;
        mock.Setup(t => t.SendAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>()))
            .Callback<string, int, string>((_, _, msg) => captured = msg)
            .ReturnsAsync("ACK");

        await BuildService(mock).SendAlarmAsync(MakeDto(), MakeSettings());

        Assert.Contains("SIA-DCS", captured);
    }

    [Fact]
    public async Task SendAlarmAsync_ReturnsTransmitterResponse()
    {
        var mock = new Mock<ISiaTransmitter>();
        mock.Setup(t => t.SendAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>()))
            .ReturnsAsync("ACK \"SIA-DCS\" 0001");

        string result = await BuildService(mock).SendAlarmAsync(MakeDto(), MakeSettings());
        Assert.Equal("ACK \"SIA-DCS\" 0001", result);
    }

    [Fact]
    public async Task SendAlarmAsync_FrameCarriesWebhookTimestamp_NotTransmissionTime()
    {
        var mock = new Mock<ISiaTransmitter>();
        string? captured = null;
        mock.Setup(t => t.SendAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>()))
            .Callback<string, int, string>((_, _, msg) => captured = msg)
            .ReturnsAsync("ACK");

        var dto = MakeDto();
        dto.Timestamp = new DateTimeOffset(2026, 7, 1, 8, 30, 15, TimeSpan.Zero);
        await BuildService(mock).SendAlarmAsync(dto, MakeSettings());

        // SiaMessageBuilder renders the timestamp as _HH:mm:ss,MM-dd-yyyy
        Assert.Contains("_08:30:15,07-01-2026", captured);
    }

    // ── Encryption wiring ─────────────────────────────────────────────

    [Fact]
    public async Task SendAlarmAsync_WithEncryptionKey_PayloadContainsEncryptedFlag()
    {
        // When an encryption key is configured the SIA-DCS token must be prefixed with *
        var mock = new Mock<ISiaTransmitter>();
        string? captured = null;
        mock.Setup(t => t.SendAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>()))
            .Callback<string, int, string>((_, _, msg) => captured = msg)
            .ReturnsAsync("ACK");

        // 32 ASCII '0' chars = 32-byte key
        string key32 = new string('0', 32);
        await BuildService(mock).SendAlarmAsync(MakeDto(), MakeSettings(encryptionKey: key32));

        Assert.Contains("*SIA-DCS", captured);
    }

    [Fact]
    public async Task SendAlarmAsync_WithoutEncryptionKey_PayloadHasNoEncryptedFlag()
    {
        var mock = new Mock<ISiaTransmitter>();
        string? captured = null;
        mock.Setup(t => t.SendAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>()))
            .Callback<string, int, string>((_, _, msg) => captured = msg)
            .ReturnsAsync("ACK");

        await BuildService(mock).SendAlarmAsync(MakeDto(), MakeSettings(encryptionKey: null));

        Assert.DoesNotContain("*SIA-DCS", captured);
    }

    // ── Sequence counter ──────────────────────────────────────────────

    [Fact]
    public async Task SendAlarmAsync_SequenceNumberIncrementsAcrossCalls()
    {
        var mock = new Mock<ISiaTransmitter>();
        var captured = new List<string>();
        mock.Setup(t => t.SendAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>()))
            .Callback<string, int, string>((_, _, msg) => captured.Add(msg))
            .ReturnsAsync("ACK");

        var svc = BuildService(mock);
        await svc.SendAlarmAsync(MakeDto(), MakeSettings());
        await svc.SendAlarmAsync(MakeDto(), MakeSettings());

        Assert.Contains("0001", captured[0]);
        Assert.Contains("0002", captured[1]);
    }
}

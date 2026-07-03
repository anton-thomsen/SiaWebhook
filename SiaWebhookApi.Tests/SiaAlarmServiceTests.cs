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
        Timestamp      = DateTimeOffset.UtcNow,
        Location       = new Location { Latitude = "52.3676", Longitude = "4.9041" }
    };

    private static SiaAlarmService BuildService(
        Mock<ISiaTransmitter> transmitterMock,
        IDeadLetterStore? deadLetterStore = null,
        IAlertNotifier? alertNotifier = null,
        SiaDeliveryOptions? options = null) =>
        new(
            transmitterMock.Object,
            new SiaSequenceCounter(),
            deadLetterStore ?? new InMemoryDeadLetterStore(),
            alertNotifier ?? new NoopAlertNotifier(),
            options ?? new SiaDeliveryOptions { InitialRetryDelay = TimeSpan.Zero },
            NullLogger<SiaAlarmService>.Instance);

    // ── Happy path ────────────────────────────────────────────────────

    [Fact]
    public async Task SendAlarmAsync_CallsTransmitter_WithCorrectEndpoint()
    {
        var mock = new Mock<ISiaTransmitter>();
        mock.Setup(t => t.SendAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SiaTransmissionResult.Ack("ACK"));

        var svc = BuildService(mock);
        await svc.SendAlarmAsync(MakeDto(), MakeSettings());

        mock.Verify(t => t.SendAsync("127.0.0.1", 9000, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendAlarmAsync_MessageIsCorrectlyFramed()
    {
        // The final SIA message must be framed as \n{CRC}{LEN}{payload}\r
        var mock = new Mock<ISiaTransmitter>();
        string? captured = null;
        mock.Setup(t => t.SendAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, int, string, CancellationToken>((_, _, msg, _) => captured = msg)
            .ReturnsAsync(SiaTransmissionResult.Ack("ACK"));

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
        mock.Setup(t => t.SendAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, int, string, CancellationToken>((_, _, msg, _) => captured = msg)
            .ReturnsAsync(SiaTransmissionResult.Ack("ACK"));

        await BuildService(mock).SendAlarmAsync(MakeDto(), MakeSettings());

        Assert.Contains("SIA-DCS", captured);
    }

    [Fact]
    public async Task SendAlarmAsync_ReturnsTransmitterResponse()
    {
        var mock = new Mock<ISiaTransmitter>();
        mock.Setup(t => t.SendAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SiaTransmissionResult.Ack("ACK \"SIA-DCS\" 0001"));

        var result = await BuildService(mock).SendAlarmAsync(MakeDto(), MakeSettings());
        Assert.True(result.Success);
        Assert.Equal("ACK \"SIA-DCS\" 0001", result.Response);
    }

    [Fact]
    public async Task SendAlarmAsync_WhenAllAttemptsFail_DeadLettersAndAlerts()
    {
        var mock = new Mock<ISiaTransmitter>();
        mock.Setup(t => t.SendAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SiaTransmissionResult.Failed("Receiver returned NAK", "NAK"));

        var store = new InMemoryDeadLetterStore();
        var notifier = new RecordingAlertNotifier();
        var result = await BuildService(
            mock,
            store,
            notifier,
            new SiaDeliveryOptions { MaxAttempts = 3, InitialRetryDelay = TimeSpan.Zero })
            .SendAlarmAsync(MakeDto(), MakeSettings());

        Assert.False(result.Success);
        Assert.NotNull(result.DeadLetterId);
        Assert.Single(store.Saved);
        Assert.Single(notifier.Alerts);
        Assert.Equal(3, result.AttemptErrors.Count);
    }

    [Fact]
    public async Task SendAlarmAsync_WhenSecondAttemptSucceeds_DoesNotDeadLetter()
    {
        var mock = new Mock<ISiaTransmitter>();
        mock.SetupSequence(t => t.SendAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SiaTransmissionResult.Failed("timeout"))
            .ReturnsAsync(SiaTransmissionResult.Ack("ACK"));

        var store = new InMemoryDeadLetterStore();
        var result = await BuildService(
            mock,
            store,
            options: new SiaDeliveryOptions { MaxAttempts = 3, InitialRetryDelay = TimeSpan.Zero })
            .SendAlarmAsync(MakeDto(), MakeSettings());

        Assert.True(result.Success);
        Assert.Empty(store.Saved);
    }

    // ── Encryption wiring ─────────────────────────────────────────────

    [Fact]
    public async Task SendAlarmAsync_WithEncryptionKey_PayloadContainsEncryptedFlag()
    {
        // When an encryption key is configured the SIA-DCS token must be prefixed with *
        var mock = new Mock<ISiaTransmitter>();
        string? captured = null;
        mock.Setup(t => t.SendAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, int, string, CancellationToken>((_, _, msg, _) => captured = msg)
            .ReturnsAsync(SiaTransmissionResult.Ack("ACK"));

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
        mock.Setup(t => t.SendAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, int, string, CancellationToken>((_, _, msg, _) => captured = msg)
            .ReturnsAsync(SiaTransmissionResult.Ack("ACK"));

        await BuildService(mock).SendAlarmAsync(MakeDto(), MakeSettings(encryptionKey: null));

        Assert.DoesNotContain("*SIA-DCS", captured);
    }

    // ── Sequence counter ──────────────────────────────────────────────

    [Fact]
    public async Task SendAlarmAsync_SequenceNumberIncrementsAcrossCalls()
    {
        var mock = new Mock<ISiaTransmitter>();
        var captured = new List<string>();
        mock.Setup(t => t.SendAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, int, string, CancellationToken>((_, _, msg, _) => captured.Add(msg))
            .ReturnsAsync(SiaTransmissionResult.Ack("ACK"));

        var svc = BuildService(mock);
        await svc.SendAlarmAsync(MakeDto(), MakeSettings());
        await svc.SendAlarmAsync(MakeDto(), MakeSettings());

        Assert.Contains("0001", captured[0]);
        Assert.Contains("0002", captured[1]);
    }

    private sealed class NoopAlertNotifier : IAlertNotifier
    {
        public Task NotifyDeadLetteredAsync(DeadLetteredAlarm alarm, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class RecordingAlertNotifier : IAlertNotifier
    {
        public List<DeadLetteredAlarm> Alerts { get; } = [];

        public Task NotifyDeadLetteredAsync(DeadLetteredAlarm alarm, CancellationToken cancellationToken = default)
        {
            Alerts.Add(alarm);
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryDeadLetterStore : IDeadLetterStore
    {
        public List<DeadLetteredAlarm> Saved { get; } = [];

        public Task<string> SaveAsync(DeadLetteredAlarm alarm, CancellationToken cancellationToken = default)
        {
            Saved.Add(alarm);
            return Task.FromResult(alarm.Id);
        }

        public Task<IReadOnlyList<DeadLetteredAlarm>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DeadLetteredAlarm>>(Saved);

        public Task UpdateAsync(DeadLetteredAlarm alarm, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task DeleteAsync(string id, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}

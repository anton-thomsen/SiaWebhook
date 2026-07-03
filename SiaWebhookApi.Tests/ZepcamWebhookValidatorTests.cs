using SiaWebhookApi.Models;
using SiaWebhookApi.Services;

namespace SiaWebhookApi.Tests;

public class ZepcamWebhookValidatorTests
{
    private static DataTransferObject ValidDto() => new()
    {
        UserName = "John",
        UserExternalId = "U001",
        Issuer = "ZEPCAM",
        Timestamp = DateTimeOffset.UtcNow,
        Location = new Location { Latitude = "52.3676", Longitude = "4.9041" }
    };

    [Fact]
    public void Validate_ValidPayload_ReturnsValid()
    {
        var result = ZepcamWebhookValidator.Validate(ValidDto());
        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("Jane\rInjected")]
    [InlineData("Jane[Injected]")]
    [InlineData("Jane|Injected")]
    [InlineData("Jane_Injected")]
    public void Validate_IdentityDelimiter_ReturnsInvalid(string userName)
    {
        var dto = ValidDto();
        dto.UserName = userName;

        var result = ZepcamWebhookValidator.Validate(dto);

        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData("north", "4.9041")]
    [InlineData("91", "4.9041")]
    [InlineData("52.3676", "west")]
    [InlineData("52.3676", "181")]
    public void Validate_InvalidCoordinates_ReturnsInvalid(string latitude, string longitude)
    {
        var dto = ValidDto();
        dto.Location = new Location { Latitude = latitude, Longitude = longitude };

        var result = ZepcamWebhookValidator.Validate(dto);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void EnsurePayloadFits_OverLimit_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            ZepcamWebhookValidator.EnsurePayloadFits(new string('A', ZepcamWebhookValidator.MaxSiaPayloadBytes + 1)));
    }

    [Fact]
    public void Validate_MissingTimestamp_ReturnsInvalid()
    {
        var dto = ValidDto();
        dto.Timestamp = default;

        var result = ZepcamWebhookValidator.Validate(dto);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_StaleTimestamp_ReturnsInvalid()
    {
        var dto = ValidDto();
        dto.Timestamp = DateTimeOffset.UtcNow.Subtract(ZepcamWebhookValidator.MaxTimestampAge).AddMinutes(-1);

        var result = ZepcamWebhookValidator.Validate(dto);

        Assert.False(result.IsValid);
    }
}

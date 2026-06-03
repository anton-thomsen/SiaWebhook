using SiaWebhookApi.Services;

namespace SiaWebhookApi.Tests;

public class SiaMathTests
{
    // ── CalculateLength ──────────────────────────────────────────────

    [Fact]
    public void CalculateLength_EmptyString_ReturnsZero()
    {
        Assert.Equal("0000", SiaMath.CalculateLength(""));
    }

    [Theory]
    [InlineData("A", "0001")]
    [InlineData("ABCDE", "0005")]
    [InlineData("1234567890", "000A")]
    public void CalculateLength_KnownInputs_ReturnsCorrectHex(string input, string expected)
    {
        Assert.Equal(expected, SiaMath.CalculateLength(input));
    }

    [Fact]
    public void CalculateLength_256Chars_Returns0100()
    {
        Assert.Equal("0100", SiaMath.CalculateLength(new string('X', 256)));
    }

    [Fact]
    public void CalculateLength_AlwaysStartsWithZero()
    {
        // SIA DC-09 spec requires the 4-char length field to start with '0'
        string result = SiaMath.CalculateLength("some payload");
        Assert.StartsWith("0", result);
        Assert.Equal(4, result.Length);
    }

    // ── CalculateCrc ─────────────────────────────────────────────────

    [Fact]
    public void CalculateCrc_EmptyString_ReturnsZero()
    {
        // No bytes processed — CRC stays at its initial value of 0x0000
        Assert.Equal("0000", SiaMath.CalculateCrc(""));
    }

    [Fact]
    public void CalculateCrc_StandardCheckVector_ReturnsKnownValue()
    {
        // CRC-16/ARC (poly 0xA001, init 0x0000) check value for "123456789" is 0xBB3D —
        // this is the industry-standard verification value for this algorithm variant.
        Assert.Equal("BB3D", SiaMath.CalculateCrc("123456789"));
    }

    [Fact]
    public void CalculateCrc_AlwaysReturns4UppercaseHexChars()
    {
        string crc = SiaMath.CalculateCrc("any payload data here");
        Assert.Equal(4, crc.Length);
        Assert.Matches("^[0-9A-F]{4}$", crc);
    }

    [Fact]
    public void CalculateCrc_DifferentInputs_ProduceDifferentValues()
    {
        string a = SiaMath.CalculateCrc("payload one");
        string b = SiaMath.CalculateCrc("payload two");
        Assert.NotEqual(a, b);
    }
}

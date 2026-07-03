using System.Security.Cryptography;
using System.Text;
using SiaWebhookApi.Services;

namespace SiaWebhookApi.Tests;

public class SiaCryptoServiceTests
{
    private static readonly byte[] ZeroKey = new byte[32]; // all-zeros 256-bit key for deterministic tests

    // ── Key validation ────────────────────────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(16)]
    [InlineData(31)]
    [InlineData(33)]
    public void Constructor_WrongKeyLength_Throws(int keyLength)
    {
        Assert.Throws<ArgumentException>(() => new SiaCryptoService(new byte[keyLength]));
    }

    [Fact]
    public void Constructor_32ByteKey_DoesNotThrow()
    {
        _ = new SiaCryptoService(new byte[32]);
    }

    // ── Output format ─────────────────────────────────────────────────

    [Fact]
    public void EncryptDataRegion_OutputIsUppercaseHex()
    {
        var svc = new SiaCryptoService(ZeroKey);
        string hex = svc.EncryptDataRegion("test");
        Assert.Matches("^[0-9A-F]+$", hex);
    }

    [Fact]
    public void EncryptDataRegion_OutputLengthIsMultipleOf32()
    {
        // AES block = 16 bytes = 32 hex chars; output must always be a whole number of blocks
        var svc = new SiaCryptoService(ZeroKey);
        string hex = svc.EncryptDataRegion("hello world");
        Assert.Equal(0, hex.Length % 32);
    }

    // ── Padding edge cases ────────────────────────────────────────────

    [Fact]
    public void EncryptDataRegion_DataAlreadyMultipleOf16_Adds16ExtraPadBytes()
    {
        // 15-char input + "|" separator = 16 bytes (a multiple of 16).
        // Spec says we must still add 16 pad bytes → total = 32 bytes → 64 hex chars.
        var svc = new SiaCryptoService(ZeroKey);
        string hex = svc.EncryptDataRegion(new string('A', 15));
        Assert.Equal(64, hex.Length);
    }

    [Fact]
    public void EncryptDataRegion_SingleChar_FitsInOneBlock()
    {
        // 1 char + "|" = 2 bytes; needs 14 pad bytes → total = 16 bytes → 32 hex chars.
        var svc = new SiaCryptoService(ZeroKey);
        string hex = svc.EncryptDataRegion("A");
        Assert.Equal(32, hex.Length);
    }

    // ── Round-trip ────────────────────────────────────────────────────

    [Theory]
    [InlineData("hello")]
    [InlineData("#A001|NPAA001][G52.3676,4.9041][ZJohn|U001|ZEPCAM]_12:00:00,01-01-2025")]
    public void EncryptDataRegion_RoundTrip_RecoverOriginalData(string original)
    {
        // Decrypt the output using raw AES-CBC with IV=0 to verify the padded plaintext
        // ends with "|{original}" — this validates both the encryption and padding structure.
        var svc = new SiaCryptoService(ZeroKey);
        string hex = svc.EncryptDataRegion(original);

        byte[] cipherBytes = Convert.FromHexString(hex);
        byte[] iv = new byte[16]; // all-zeros IV as per SIA spec 5.4.6

        using var aes = Aes.Create();
        aes.Key = ZeroKey;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.None;

        using var decryptor = aes.CreateDecryptor();
        byte[] plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
        string plain = Encoding.ASCII.GetString(plainBytes);

        Assert.EndsWith("|" + original, plain);
    }
}

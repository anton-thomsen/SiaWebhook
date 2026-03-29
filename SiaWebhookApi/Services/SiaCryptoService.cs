using System.Security.Cryptography;
using System.Text;

namespace SiaWebhookApi.Services;

public class SiaCryptoService
{
    private readonly byte[] _key;

    // The key must be exactly 32 bytes = 256 bits for AES-256
    public SiaCryptoService(byte[] key)
    {
        if (key.Length != 32)
            throw new ArgumentException("AES-256 key must be exactly 32 bytes.");

        _key = key;
    }

    // Takes the raw data region (everything that goes between [ and \r)
    // Returns it encrypted and hex encoded, ready to slot into the message
    public string EncryptDataRegion(string dataRegion)
    {
        // 1. Add random padding so total length is a multiple of 16
        string padded = AddPadding(dataRegion);

        // 2. Convert to bytes so we can encrypt
        byte[] plaintextBytes = Encoding.ASCII.GetBytes(padded);

        // 3. Encrypt with AES-256-CBC, IV is all zeros as per spec section 5.4.6
        byte[] encryptedBytes = Encrypt(plaintextBytes);

        // 4. Hex encode every byte as 2 uppercase characters (e.g. 0xAB → "AB")
        // This is required by spec section 5.4.7
        return Convert.ToHexString(encryptedBytes);
    }

    private string AddPadding(string dataRegion)
    {
        // The spec says the encrypted region must be a multiple of 16 bytes
        // Padding bytes go at the START, terminated by a | character
        // Spec 5.4.4.2: pad bytes can be 0-255 but NOT |, [ or ]
        int totalLength = Encoding.ASCII.GetByteCount(dataRegion) + 1; // +1 for the | separator
        int padBytesNeeded = (16 - (totalLength % 16)) % 16;

        // If already a multiple of 16, the spec says we must still add 16 pad bytes
        if (padBytesNeeded == 0) padBytesNeeded = 16;

        byte[] padBytes = GeneratePadBytes(padBytesNeeded);
        string pad = Encoding.ASCII.GetString(padBytes);

        // Structure: <pad bytes>|<original data>
        return pad + "|" + dataRegion;
    }

    private byte[] GeneratePadBytes(int count)
    {
        // Forbidden characters per spec 5.4.4.2: | (0x7C), [ (0x5B), ] (0x5D)
        byte[] forbidden = { 0x7C, 0x5B, 0x5D };
        byte[] result = new byte[count];
        int filled = 0;

        while (filled < count)
        {
            byte candidate = (byte)RandomNumberGenerator.GetInt32(0, 256);
            if (!forbidden.Contains(candidate))
                result[filled++] = candidate;
        }

        return result;
    }

    private byte[] Encrypt(byte[] plaintext)
    {
        // IV = all zeros as specified in section 5.4.6
        byte[] iv = new byte[16];

        using var aes = Aes.Create();
        aes.KeySize = 256;
        aes.Key = _key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.None; // We handle padding ourselves above

        using var encryptor = aes.CreateEncryptor();
        return encryptor.TransformFinalBlock(plaintext, 0, plaintext.Length);
    }
}

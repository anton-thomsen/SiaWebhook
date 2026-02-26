using System;
using System.Text;

/// <summary>
/// Utility class for SIA DC-09 protocol calculations. 
/// Isolates the byte-level math from the routing logic for clean architecture and easy unit testing.
/// </summary>
public static class SiaMath
{
    /// <summary>
    /// Calculates the SIA DC-09 message length.
    /// The length must be formatted as '0' followed by 3 uppercase hex digits (e.g., "003A").
    /// </summary>
    /// <param name="targetString">The string starting from the first quote of the ID token up to the timestamp.</param>
    public static string CalculateLength(string targetString)
    {
        // 1. Get the actual byte count. We use ASCII because C# strings are UTF-16 by default, 
        // and the SIA protocol strictly operates on ASCII bytes.
       int byteCount = Encoding.ASCII.GetByteCount(targetString);
       
        // 2. Format as a 4-character hex string starting with '0'. 
        // "X3" tells C# to format the integer as a 3-digit uppercase hex string.
       return $"0{byteCount:X3}";
    }
    /// <summary>
    /// Calculates the CRC-16 checksum for the SIA message to ensure data integrity over the network.
    /// Uses the standard CCITT polynomial (0x1021).
    /// </summary>
    /// <param name="targetString">The exact same target string used for the length calculation.</param>
    public static string CalculateCrc(string targetString)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(targetString);

        // 1. Standard starting value for this specific crc Algorithm.
        ushort crc = 0x0000;

        // 2. The "Polynomial". Think of this as a mathematical mask used to scramble the data predictably.
        ushort polynomial = 0x1021;

        // 3. Loop through every single byte in your target string
        foreach (byte b in bytes)
        {
            
            // XOR (^) the byte into the top 8 bits of the 16-bit CRC
            crc ^= (ushort)(b << 8);

            // 4. Loop through every single byte
            for (int i = 0; i < 8; i++)
            {
                // 5. check if the first bit is 1 or 0
                if ((crc & 0x8000) != 0)
                {
                    // 6. If it's 1 then it will:
                    // Move the whole crc one bit to the left,
                    // The far right of the crc becomes 0,
                    // Then we do a scramble using the polynomial 0x1021.
                    crc = (ushort)((crc << 1) ^ polynomial);
                }
                else
                {
                    // 7. If the first bit is 0 then don't scramble and just move on step to the left. 
                    crc <<= 1;
                }
            }
        }
        // 8. When the whole scramble thing is done then we return the crc value to a Hexdecimal string with 4 characters
        return crc.ToString("X4");
    }
    
}

using System;
using System.Security.Cryptography;
using System.Text;

namespace FamilyTimeMultiplayer.Core.Transport.Relay;

public static class RoomCode
{
    public const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const int Letters = 6;

    public static string Generate()
    {
        var code = new StringBuilder(Letters + 1);
        byte[] random = new byte[Letters];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(random);
        }
        for (int i = 0; i < Letters; i++)
        {
            if (i == Letters / 2)
            {
                code.Append('-');
            }
            code.Append(Alphabet[random[i] % Alphabet.Length]);
        }
        return code.ToString();
    }

    public static string NewSecret()
    {
        byte[] random = new byte[16];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(random);
        }
        return BitConverter.ToString(random).Replace("-", string.Empty);
    }

    public static bool TryNormalize(string? text, out string code)
    {
        code = string.Empty;
        if (text == null)
        {
            return false;
        }
        string compact = text.Trim().Replace("-", string.Empty).Replace(" ", string.Empty).ToUpperInvariant();
        if (compact.Length != Letters)
        {
            return false;
        }
        foreach (char c in compact)
        {
            if (Alphabet.IndexOf(c) < 0)
            {
                return false;
            }
        }
        code = compact.Substring(0, Letters / 2) + "-" + compact.Substring(Letters / 2);
        return true;
    }
}

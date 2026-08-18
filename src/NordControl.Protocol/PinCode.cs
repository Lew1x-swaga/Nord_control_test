using System;

namespace NordControl.Protocol;

public static class PinCode
{
    // Skip 0/O/1/I/L so a PIN on the board is readable from the back of the room.
    public const string DigitAlphabet = "23456789";
    public const string LetterAlphabet = "ABCDEFGHJKMNPQRSTUVWXYZ";

    public static string Generate(Random? rng = null)
    {
        rng ??= Random.Shared;
        var chars = new char[ProtocolConstants.PinLength];
        for (var i = 0; i < ProtocolConstants.PinDigitCount; i++)
        {
            chars[i] = DigitAlphabet[rng.Next(DigitAlphabet.Length)];
        }

        for (var i = 0; i < ProtocolConstants.PinLetterCount; i++)
        {
            chars[ProtocolConstants.PinDigitCount + i] = LetterAlphabet[rng.Next(LetterAlphabet.Length)];
        }

        for (var i = chars.Length - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }

        return new string(chars);
    }

    public static string Normalize(string? pin)
    {
        return string.IsNullOrWhiteSpace(pin) ? string.Empty : pin.Trim().ToUpperInvariant();
    }

    public static bool IsWellFormed(string? pin)
    {
        var normalized = Normalize(pin);
        if (normalized.Length != ProtocolConstants.PinLength)
        {
            return false;
        }

        var digits = 0;
        var letters = 0;
        foreach (var c in normalized)
        {
            if (char.IsDigit(c))
            {
                digits++;
            }
            else if (c is >= 'A' and <= 'Z')
            {
                letters++;
            }
            else
            {
                return false;
            }
        }

        return digits == ProtocolConstants.PinDigitCount && letters == ProtocolConstants.PinLetterCount;
    }

    public static bool Equals(string? left, string? right)
    {
        return string.Equals(Normalize(left), Normalize(right), StringComparison.Ordinal);
    }
}

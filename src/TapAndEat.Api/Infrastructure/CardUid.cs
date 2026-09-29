using System.Text.RegularExpressions;

namespace TapAndEat.Api.Infrastructure;

/// <summary>
/// RFID readers print UIDs in different shapes ("04 a1 b2 c3", "04:A1:B2:C3",
/// "04A1B2C3"). Everything is stored and compared in one canonical form:
/// upper-case hex, no separators.
/// </summary>
public static partial class CardUid
{
    [GeneratedRegex("^[0-9A-F]{4,32}$")]
    private static partial Regex HexPattern();

    public static bool TryNormalize(string? raw, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(raw)) return false;

        var cleaned = new string(raw.Where(c => !char.IsWhiteSpace(c) && c != ':' && c != '-').ToArray())
            .ToUpperInvariant();
        if (!HexPattern().IsMatch(cleaned)) return false;

        normalized = cleaned;
        return true;
    }
}

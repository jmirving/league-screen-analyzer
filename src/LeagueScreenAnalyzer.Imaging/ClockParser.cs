using System.Globalization;

namespace LeagueScreenAnalyzer.Imaging;

public static class ClockParser
{
    public static bool TryParse(
        string? text,
        TimeSpan maximumGameTime,
        out TimeSpan gameTime,
        out string? reason)
    {
        gameTime = default;
        reason = null;
        if (string.IsNullOrEmpty(text))
        {
            reason = "No clock text was recognized.";
            return false;
        }

        if (text != text.Trim())
        {
            reason = "Clock text contains leading or trailing noise.";
            return false;
        }

        string[] parts = text.Split(':');
        if (parts.Length != 2 || parts[0].Length is < 1 or > 3 || parts[1].Length != 2)
        {
            reason = "Clock text must match M:SS, MM:SS, or MMM:SS.";
            return false;
        }

        if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int minutes) ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int seconds))
        {
            reason = "Clock text contains a non-decimal character.";
            return false;
        }

        if (seconds is < 0 or > 59)
        {
            reason = "Clock seconds must be between 00 and 59.";
            return false;
        }

        gameTime = TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(seconds);
        if (gameTime > maximumGameTime)
        {
            reason = "Clock exceeds the profile maximum game time.";
            gameTime = default;
            return false;
        }

        return true;
    }
}

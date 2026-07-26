using System.Globalization;

namespace LeagueScreenAnalyzer.Core.Models;

public sealed record ClockSampleLabel
{
    internal ClockSampleLabel(string value, int totalSeconds)
    {
        Value = value;
        TotalSeconds = totalSeconds;
    }

    public string Value { get; }

    public int TotalSeconds { get; }

    public long TotalMilliseconds => TotalSeconds * 1000L;
}

public static class ClockSampleLabelParser
{
    public static bool TryParse(
        string? input,
        out ClockSampleLabel? label,
        out string? validationMessage)
    {
        label = null;
        validationMessage = null;
        string value = input?.Trim() ?? string.Empty;
        if (value.Length == 0)
        {
            validationMessage =
                "Enter the visible clock value as M:SS or MM:SS, or explicitly choose an unlabeled diagnostic save.";
            return false;
        }

        string[] parts = value.Split(':');
        if (parts.Length != 2)
        {
            validationMessage =
                "Use one colon in the clock value, for example 3:40 or 10:00.";
            return false;
        }

        if (parts[0].Length is < 1 or > 2 ||
            !parts[0].All(character => character is >= '0' and <= '9'))
        {
            validationMessage =
                "Minutes must be one or two non-negative digits, for example 3 or 10.";
            return false;
        }

        if (parts[1].Length != 2 ||
            !parts[1].All(character => character is >= '0' and <= '9'))
        {
            validationMessage =
                "Seconds must be exactly two digits from 00 through 59.";
            return false;
        }

        int minutes = int.Parse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture);
        int seconds = int.Parse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture);
        if (seconds > 59)
        {
            validationMessage =
                "Seconds must be from 00 through 59; enter the visible time as M:SS or MM:SS.";
            return false;
        }

        label = new ClockSampleLabel(
            string.Create(CultureInfo.InvariantCulture, $"{minutes}:{seconds:00}"),
            checked((minutes * 60) + seconds));
        return true;
    }
}

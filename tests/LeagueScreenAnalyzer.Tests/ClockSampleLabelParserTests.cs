using LeagueScreenAnalyzer.Core.Models;

namespace LeagueScreenAnalyzer.Tests;

public sealed class ClockSampleLabelParserTests
{
    [Theory]
    [InlineData("0:00", "0:00", 0)]
    [InlineData("3:40", "3:40", 220)]
    [InlineData("9:59", "9:59", 599)]
    [InlineData("10:00", "10:00", 600)]
    [InlineData("29:59", "29:59", 1799)]
    [InlineData("  3:40 \t", "3:40", 220)]
    [InlineData("03:40", "3:40", 220)]
    public void TryParse_NormalizesValidLabels(
        string input,
        string expected,
        int expectedSeconds)
    {
        bool parsed = ClockSampleLabelParser.TryParse(
            input,
            out ClockSampleLabel? label,
            out string? validationMessage);

        Assert.True(parsed);
        Assert.Null(validationMessage);
        Assert.NotNull(label);
        Assert.Equal(expected, label.Value);
        Assert.Equal(expectedSeconds, label.TotalSeconds);
        Assert.Equal(expectedSeconds * 1000L, label.TotalMilliseconds);
    }

    [Theory]
    [InlineData("3:60", "00 through 59")]
    [InlineData("3:99", "00 through 59")]
    [InlineData("3-40", "one colon")]
    [InlineData("3::40", "one colon")]
    [InlineData("3;40", "one colon")]
    [InlineData("-1:00", "non-negative")]
    [InlineData("", "explicitly choose")]
    [InlineData("   ", "explicitly choose")]
    public void TryParse_RejectsInvalidLabels(string input, string expectedMessage)
    {
        bool parsed = ClockSampleLabelParser.TryParse(
            input,
            out ClockSampleLabel? label,
            out string? validationMessage);

        Assert.False(parsed);
        Assert.Null(label);
        Assert.Contains(expectedMessage, validationMessage, StringComparison.OrdinalIgnoreCase);
    }
}

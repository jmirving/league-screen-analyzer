using LeagueScreenAnalyzer.Imaging;

namespace LeagueScreenAnalyzer.Tests;

public sealed class ClockParserTests
{
    [Theory]
    [InlineData("0:00", 0)]
    [InlineData("9:59", 599)]
    [InlineData("10:00", 600)]
    [InlineData("12:43", 763)]
    public void TryParse_AcceptsConstrainedClock(string text, int seconds)
    {
        Assert.True(ClockParser.TryParse(text, TimeSpan.FromHours(3), out TimeSpan result, out _));
        Assert.Equal(TimeSpan.FromSeconds(seconds), result);
    }

    [Theory]
    [InlineData("12.43")]
    [InlineData("12-43")]
    [InlineData("12:78")]
    [InlineData(" 12:43")]
    [InlineData("12:43 ")]
    [InlineData("x12:43")]
    [InlineData("12:4")]
    public void TryParse_RejectsMalformedInput(string text)
    {
        Assert.False(ClockParser.TryParse(text, TimeSpan.FromHours(3), out _, out string? reason));
        Assert.False(string.IsNullOrWhiteSpace(reason));
    }

    [Fact]
    public void TryParse_EnforcesMaximum()
    {
        Assert.False(ClockParser.TryParse("60:01", TimeSpan.FromHours(1), out _, out _));
        Assert.True(ClockParser.TryParse("60:00", TimeSpan.FromHours(1), out _, out _));
    }
}

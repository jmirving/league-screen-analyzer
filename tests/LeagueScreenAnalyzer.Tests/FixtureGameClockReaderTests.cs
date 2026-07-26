using LeagueScreenAnalyzer.Capture.Fixtures;
using LeagueScreenAnalyzer.Core.Models;

namespace LeagueScreenAnalyzer.Tests;

public sealed class FixtureGameClockReaderTests
{
    [Fact]
    public async Task ReadAsync_AcceptsValidProgression()
    {
        FixtureGameClockReader reader = new();

        ClockReading first = await reader.ReadAsync(CreateClockFrame(1, 0, "12:43"));
        ClockReading second = await reader.ReadAsync(CreateClockFrame(2, 1000, "12:44"));

        Assert.Equal(ClockReadingStatus.Valid, first.Status);
        Assert.Equal(TimeSpan.FromMinutes(12) + TimeSpan.FromSeconds(44), second.GameTime);
    }

    [Fact]
    public async Task ReadAsync_AcceptsRepeatedClockValue()
    {
        FixtureGameClockReader reader = new();

        await reader.ReadAsync(CreateClockFrame(1, 0, "12:43"));
        ClockReading repeated = await reader.ReadAsync(CreateClockFrame(2, 500, "12:43"));

        Assert.Equal(ClockReadingStatus.Valid, repeated.Status);
        Assert.Equal(TimeSpan.FromMinutes(12) + TimeSpan.FromSeconds(43), repeated.GameTime);
    }

    [Fact]
    public async Task ReadAsync_AcceptsMinuteRollover()
    {
        FixtureGameClockReader reader = new();

        await reader.ReadAsync(CreateClockFrame(1, 0, "12:59"));
        ClockReading rollover = await reader.ReadAsync(CreateClockFrame(2, 1000, "13:00"));

        Assert.Equal(ClockReadingStatus.Valid, rollover.Status);
        Assert.Equal(TimeSpan.FromMinutes(13), rollover.GameTime);
    }

    [Fact]
    public async Task ReadAsync_ReturnsNotVisibleForMissingClock()
    {
        FixtureGameClockReader reader = new();

        ClockReading reading = await reader.ReadAsync(CreateClockFrame(1, 0, null, false));

        Assert.Equal(ClockReadingStatus.NotVisible, reading.Status);
        Assert.Null(reading.GameTime);
    }

    [Fact]
    public async Task ReadAsync_RejectsBackwardJump()
    {
        FixtureGameClockReader reader = new();

        await reader.ReadAsync(CreateClockFrame(1, 0, "12:43"));
        ClockReading reading = await reader.ReadAsync(CreateClockFrame(2, 1000, "12:42"));

        Assert.Equal(ClockReadingStatus.Backward, reading.Status);
        Assert.Null(reading.GameTime);
    }

    [Fact]
    public async Task ReadAsync_RejectsImplausiblyLargeForwardJump()
    {
        FixtureGameClockReader reader = new();

        await reader.ReadAsync(CreateClockFrame(1, 0, "12:43"));
        ClockReading reading = await reader.ReadAsync(CreateClockFrame(2, 1000, "20:00"));

        Assert.Equal(ClockReadingStatus.Implausible, reading.Status);
    }

    internal static RegionFrame CreateClockFrame(
        long sequence,
        long sourceTimeMs,
        string? clockText,
        bool clockVisible = true,
        bool mapVisible = true) =>
        new(
            RegionType.Clock,
            sequence,
            TimeSpan.FromMilliseconds(sourceTimeMs),
            200,
            80,
            new FixtureFramePayload(clockText, clockVisible, mapVisible));
}

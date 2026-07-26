using LeagueScreenAnalyzer.Core.Models;
using LeagueScreenAnalyzer.Imaging;
using LeagueScreenAnalyzer.Storage;

namespace LeagueScreenAnalyzer.Tests;

public sealed class ClockProfileTests
{
    [Fact]
    public void BuiltInProfile_IsStableAndValid()
    {
        ClockRecognitionProfile profile =
            BuiltInClockProfiles.Get(BuiltInClockProfiles.LeagueReplayV1Id);

        Assert.Equal("league-replay-v1", profile.Id);
        Assert.Equal(1, profile.Version);
        Assert.Equal(ClockValidationMode.ReplayContinuous, profile.ValidationMode);
        Assert.Same(profile, profile.Validate());
    }

    [Fact]
    public async Task CaptureLayout_PersistsOptionalProfileReference()
    {
        using TemporaryDirectory temporary = new();
        JsonCaptureLayoutStore store = new(temporary.Path);
        NormalizedRegion region = new(0.1, 0.1, 0.2, 0.2);
        CaptureLayout layout = new(
            "profile-layout",
            region,
            region,
            16d / 9,
            BuiltInClockProfiles.LeagueReplayV1Id);

        await store.SaveAsync(layout, overwrite: false);
        CaptureLayout loaded = await store.LoadAsync(layout.Name);

        Assert.Equal(BuiltInClockProfiles.LeagueReplayV1Id, loaded.ClockProfileId);
    }

    [Fact]
    public void Profile_RejectsInvalidConfidenceAndSpeed()
    {
        ClockRecognitionProfile profile =
            BuiltInClockProfiles.Get(BuiltInClockProfiles.LeagueReplayV1Id);
        Assert.Throws<ArgumentException>(() => (profile with { MinimumRecognitionConfidence = 1.1 }).Validate());
        Assert.Throws<ArgumentException>(() => (profile with { PlaybackSpeed = 0 }).Validate());
    }
}

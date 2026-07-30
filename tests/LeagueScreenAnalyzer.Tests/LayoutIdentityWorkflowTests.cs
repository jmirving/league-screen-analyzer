using LeagueScreenAnalyzer.Core.Models;
using LeagueScreenAnalyzer.Storage;

namespace LeagueScreenAnalyzer.Tests;

public sealed class LayoutIdentityWorkflowTests
{
    [Fact]
    public async Task SaveAs_CreatesIndependentLayout_AndRetainsBothRegionsAndProfile()
    {
        using TemporaryDirectory directory = new();
        JsonCaptureLayoutStore store = new(directory.Path);
        CaptureLayout original = Layout(
            "A",
            new NormalizedRegion(0.4, 0.01, 0.2, 0.1),
            new NormalizedRegion(0.7, 0.7, 0.2, 0.2));
        await store.SaveAsync(original, overwrite: false);
        CaptureLayout modified = Layout(
            "B",
            new NormalizedRegion(0.35, 0.02, 0.24, 0.1),
            new NormalizedRegion(0.65, 0.65, 0.25, 0.25));

        await store.SaveAsync(modified, overwrite: false);

        Assert.Equal(original, await store.LoadAsync("A"));
        Assert.Equal(modified, await store.LoadAsync("B"));
        Assert.Equal("league-replay-v3", (await store.LoadAsync("B")).ClockProfileId);
    }

    [Fact]
    public async Task ExistingSaveAsTarget_RequiresOverwriteAndFailureChangesNothing()
    {
        using TemporaryDirectory directory = new();
        JsonCaptureLayoutStore store = new(directory.Path);
        CaptureLayout original = Layout(
            "A",
            new NormalizedRegion(0.4, 0.01, 0.2, 0.1),
            new NormalizedRegion(0.7, 0.7, 0.2, 0.2));
        await store.SaveAsync(original, overwrite: false);

        await Assert.ThrowsAsync<CaptureLayoutException>(
            () => store.SaveAsync(
                Layout(
                    "A",
                    new NormalizedRegion(0.3, 0.02, 0.3, 0.1),
                    new NormalizedRegion(0.6, 0.6, 0.25, 0.25)),
                overwrite: false));

        Assert.Equal(original, await store.LoadAsync("A"));
    }

    private static CaptureLayout Layout(
        string name,
        NormalizedRegion clock,
        NormalizedRegion minimap) =>
        new(name, clock, minimap, 16d / 9, "league-replay-v3");
}

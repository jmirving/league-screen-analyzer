using LeagueScreenAnalyzer.Core.Models;
using LeagueScreenAnalyzer.Storage;

namespace LeagueScreenAnalyzer.Tests;

public sealed class JsonCaptureLayoutStoreTests
{
    [Fact]
    public async Task SaveAndLoad_RoundTripsAndLists()
    {
        using TemporaryDirectory directory = new();
        JsonCaptureLayoutStore store = new(directory.Path);
        CaptureLayout expected = Layout("Broadcast", 16d / 9);

        await store.SaveAsync(expected, overwrite: false);
        CaptureLayout actual = await store.LoadAsync("Broadcast");
        IReadOnlyList<string> names = await store.ListAsync();

        Assert.Equal(expected, actual);
        Assert.Equal(["Broadcast"], names);
    }

    [Fact]
    public async Task MalformedJson_IsRejectedWithActionableError()
    {
        using TemporaryDirectory directory = new();
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "Broken.json"), "{ nope");
        JsonCaptureLayoutStore store = new(directory.Path);

        CaptureLayoutException error = await Assert.ThrowsAsync<CaptureLayoutException>(
            () => store.LoadAsync("Broken"));

        Assert.Contains("malformed", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UnsupportedSchema_IsRejected()
    {
        using TemporaryDirectory directory = new();
        await WriteAsync(directory, "Old", ValidJson.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 7"));
        JsonCaptureLayoutStore store = new(directory.Path);

        CaptureLayoutException error = await Assert.ThrowsAsync<CaptureLayoutException>(
            () => store.LoadAsync("Old"));

        Assert.Contains("unsupported schema", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OutOfBoundsRegion_IsRejected()
    {
        using TemporaryDirectory directory = new();
        await WriteAsync(directory, "BadBounds", ValidJson.Replace("\"width\": 0.2", "\"width\": 0.9"));
        JsonCaptureLayoutStore store = new(directory.Path);

        CaptureLayoutException error = await Assert.ThrowsAsync<CaptureLayoutException>(
            () => store.LoadAsync("BadBounds"));

        Assert.Contains("bounds", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MissingRequiredRegionFields_AreRejected()
    {
        using TemporaryDirectory directory = new();
        await WriteAsync(directory, "Incomplete", ValidJson.Replace(", \"height\": 0.2", string.Empty));
        JsonCaptureLayoutStore store = new(directory.Path);

        CaptureLayoutException error = await Assert.ThrowsAsync<CaptureLayoutException>(
            () => store.LoadAsync("Incomplete"));

        Assert.Contains("must include", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Save_RequiresExplicitOverwrite()
    {
        using TemporaryDirectory directory = new();
        JsonCaptureLayoutStore store = new(directory.Path);
        await store.SaveAsync(Layout("Broadcast"), overwrite: false);

        await Assert.ThrowsAsync<CaptureLayoutException>(
            () => store.SaveAsync(
                new CaptureLayout(
                    "Broadcast",
                    new NormalizedRegion(0.1, 0.1, 0.1, 0.1),
                    new NormalizedRegion(0.8, 0.8, 0.1, 0.1)),
                overwrite: false));

        await store.SaveAsync(
            new CaptureLayout(
                "Broadcast",
                new NormalizedRegion(0.1, 0.1, 0.1, 0.1),
                new NormalizedRegion(0.8, 0.8, 0.1, 0.1)),
            overwrite: true);
        Assert.Equal(0.1, (await store.LoadAsync("Broadcast")).ClockRegion.X);
    }

    [Fact]
    public async Task Delete_RemovesLayout()
    {
        using TemporaryDirectory directory = new();
        JsonCaptureLayoutStore store = new(directory.Path);
        await store.SaveAsync(Layout("DeleteMe"), overwrite: false);

        await store.DeleteAsync("DeleteMe");

        Assert.Empty(await store.ListAsync());
        await Assert.ThrowsAsync<CaptureLayoutException>(() => store.LoadAsync("DeleteMe"));
    }

    [Fact]
    public async Task AtomicSave_LeavesNoTemporaryFiles()
    {
        using TemporaryDirectory directory = new();
        JsonCaptureLayoutStore store = new(directory.Path);

        await store.SaveAsync(Layout("Atomic"), overwrite: false);

        Assert.Single(Directory.GetFiles(directory.Path));
        Assert.Empty(Directory.GetFiles(directory.Path, "*.tmp"));
    }

    private static CaptureLayout Layout(string name, double? aspect = null) => new(
        name,
        new NormalizedRegion(0.4, 0.01, 0.2, 0.1),
        new NormalizedRegion(0.7, 0.7, 0.2, 0.2),
        aspect);

    private static Task WriteAsync(TemporaryDirectory directory, string name, string json) =>
        File.WriteAllTextAsync(
            Path.Combine(directory.Path, $"{name}.json"),
            json.Replace("\"Test\"", $"\"{name}\""));

    private const string ValidJson =
        """
        {
          "schemaVersion": 1,
          "name": "Test",
          "clockRegion": { "x": 0.4, "y": 0.1, "width": 0.2, "height": 0.2 },
          "minimapRegion": { "x": 0.7, "y": 0.7, "width": 0.2, "height": 0.2 }
        }
        """;
}

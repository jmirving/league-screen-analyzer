using LeagueScreenAnalyzer.Core.Models;

namespace LeagueScreenAnalyzer.Imaging;

public static class BuiltInClockProfiles
{
    public const string LeagueReplayV1Id = "league-replay-v1";
    public const string LeagueReplayV2Id = "league-replay-v2";

    public static IReadOnlyList<ClockRecognitionProfile> All { get; } =
    [
        new ClockRecognitionProfile(
            LeagueReplayV1Id,
            "League Replay HUD",
            1,
            "M:SS|MM:SS",
            4,
            5,
            ClockForegroundPolarity.LightOnDark,
            ClockThresholdStrategy.Otsu,
            160,
            0.88,
            TimeSpan.FromHours(3),
            1,
            TimeSpan.FromSeconds(1.25),
            TimeSpan.Zero,
            TimeSpan.FromSeconds(3),
            12,
            ClockValidationMode.ReplayContinuous).Validate(),
        new ClockRecognitionProfile(
            LeagueReplayV2Id,
            "League Replay HUD (real calibrated v2)",
            2,
            "MM:SS",
            5,
            5,
            ClockForegroundPolarity.LightOnDark,
            ClockThresholdStrategy.Otsu,
            160,
            0.82,
            TimeSpan.FromHours(3),
            1,
            TimeSpan.FromSeconds(1.25),
            TimeSpan.Zero,
            TimeSpan.FromSeconds(3),
            12,
            ClockValidationMode.ReplayContinuous).Validate()
    ];

    public static ClockRecognitionProfile Get(string id) =>
        All.SingleOrDefault(profile => string.Equals(profile.Id, id, StringComparison.Ordinal))
        ?? LoadGeneratedProfile(id);

    private static ClockRecognitionProfile LoadGeneratedProfile(string id)
    {
        string directory;
        try
        {
            directory = ClockTemplateProfileLoader.FindProfileDirectory(id);
        }
        catch (DirectoryNotFoundException)
        {
            throw new KeyNotFoundException($"Unknown clock profile '{id}'.");
        }

        ClockTemplateManifest manifest = ClockTemplateProfileLoader.LoadManifest(directory);
        ClockRecognitionProfile baseProfile = Get(manifest.BaseProfileId);
        return (baseProfile with
        {
            Id = manifest.ProfileId,
            Name = $"{baseProfile.Name} ({manifest.ProfileId})",
            Version = manifest.ProfileVersion
        }).Validate();
    }
}

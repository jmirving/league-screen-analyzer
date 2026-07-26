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
            "League Replay HUD — v1 synthetic",
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
            "League Replay HUD — v2 real calibrated",
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
        ClockProfileCatalog.CreateDefault().Get(id).Profile;
}

namespace LeagueScreenAnalyzer.Imaging;

// Deterministic 5x7 canonical masks used by the initial profile. These are classifier
// references, not claims about the League font. Real replay crops must be calibrated
// and evaluated before this profile can be described as replay-validated.
internal static class SevenSegmentTemplates
{
    private static readonly IReadOnlyDictionary<char, string[]> Rows = new Dictionary<char, string[]>
    {
        ['0'] = ["11111", "10001", "10001", "10001", "10001", "10001", "11111"],
        ['1'] = ["00100", "01100", "00100", "00100", "00100", "00100", "01110"],
        ['2'] = ["11111", "00001", "00001", "11111", "10000", "10000", "11111"],
        ['3'] = ["11111", "00001", "00001", "11111", "00001", "00001", "11111"],
        ['4'] = ["10001", "10001", "10001", "11111", "00001", "00001", "00001"],
        ['5'] = ["11111", "10000", "10000", "11111", "00001", "00001", "11111"],
        ['6'] = ["11111", "10000", "10000", "11111", "10001", "10001", "11111"],
        ['7'] = ["11111", "00001", "00010", "00100", "01000", "01000", "01000"],
        ['8'] = ["11111", "10001", "10001", "11111", "10001", "10001", "11111"],
        ['9'] = ["11111", "10001", "10001", "11111", "00001", "00001", "11111"]
    };

    public static IEnumerable<char> Digits => Rows.Keys;

    public static bool[] Get(char digit) =>
        Rows[digit].SelectMany(row => row.Select(pixel => pixel == '1')).ToArray();
}

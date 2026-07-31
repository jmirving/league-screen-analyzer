using System.Globalization;

namespace LeagueScreenAnalyzer.Imaging;

public readonly record struct ProfileVersionKey(string Family, int Version)
{
    public static ProfileVersionKey Parse(string profileId, int declaredVersion)
    {
        ProfileVersionKey key = ParseId(profileId);
        if (declaredVersion <= 0)
        {
            throw new InvalidDataException(
                $"Profile '{profileId}' has malformed version metadata '{declaredVersion}'; versions must be positive integers.");
        }

        if (key.Version != declaredVersion)
        {
            throw new InvalidDataException(
                $"Profile '{profileId}' declares version {declaredVersion}, but its stable ID declares v{key.Version}.");
        }

        return key;
    }

    public static ProfileVersionKey Parse(
        string profileId,
        int declaredVersion,
        string? declaredFamily)
    {
        ProfileVersionKey key = Parse(profileId, declaredVersion);
        if (declaredFamily is not null &&
            !string.Equals(key.Family, declaredFamily, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Profile '{profileId}' declares family '{declaredFamily}', but its stable ID declares family '{key.Family}'.");
        }

        return key;
    }

    public static ProfileVersionKey ParseId(string profileId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        if (!TryParseId(profileId, out ProfileVersionKey key))
        {
            throw new InvalidDataException(
                $"Profile ID '{profileId}' has malformed version metadata; expected a terminal '-vN' positive canonical numeric version.");
        }

        return key;
    }

    public static bool TryParseId(string profileId, out ProfileVersionKey key)
    {
        key = default;
        if (string.IsNullOrWhiteSpace(profileId))
        {
            return false;
        }

        int marker = profileId.LastIndexOf("-v", StringComparison.Ordinal);
        if (marker <= 0 || marker == profileId.Length - 2)
        {
            return false;
        }

        string versionText = profileId[(marker + 2)..];
        if (versionText.Length > 1 && versionText[0] == '0' ||
            !versionText.All(char.IsAsciiDigit) ||
            !int.TryParse(
                versionText,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int version) ||
            version <= 0)
        {
            return false;
        }

        key = new ProfileVersionKey(profileId[..marker], version);
        return true;
    }
}

public static class ProfileVersionSelection
{
    public static T HighestCompatible<T>(
        IEnumerable<T> profiles,
        string family,
        Func<T, string> familySelector,
        Func<T, int> versionSelector)
    {
        T[] compatible = profiles
            .Where(profile => string.Equals(
                familySelector(profile),
                family,
                StringComparison.Ordinal))
            .OrderByDescending(versionSelector)
            .ToArray();
        return compatible.Length == 0
            ? throw new KeyNotFoundException(
                $"No valid profile is available in compatible family '{family}'.")
            : compatible[0];
    }

    public static IReadOnlyList<IGrouping<(string Family, int Version), T>>
        DuplicateFamilyVersions<T>(
            IEnumerable<T> profiles,
            Func<T, string> familySelector,
            Func<T, int> versionSelector) =>
        profiles
            .GroupBy(profile => (familySelector(profile), versionSelector(profile)))
            .Where(group => group.Skip(1).Any())
            .ToArray();
}

using System.Text.Json;
using LeagueScreenAnalyzer.Core.Models;

namespace LeagueScreenAnalyzer.Imaging;

public enum MinimapProfileProvenance
{
    BuiltIn,
    Packaged,
    UserInstalled,
    DevelopmentOverride
}

public sealed record MinimapProfileCatalogEntry(
    string Id,
    string DisplayName,
    int Version,
    bool IsCalibrated,
    MinimapProfileProvenance Provenance,
    string? SourcePath,
    MinimapValidationProfile Profile)
{
    public string CalibrationStatus => IsCalibrated ? "Calibrated" : "Calibration-oriented";

    public string UiLabel =>
        $"{DisplayName}  [{Id}]  v{Version}  ({CalibrationStatus})";
}

public sealed record MinimapProfileCatalogError(string? ProfilePath, string Message);

public sealed record MinimapProfileSearchRoot(
    string Directory,
    MinimapProfileProvenance Provenance);

public sealed class MinimapProfileCatalog
{
    public const string OverrideEnvironmentVariable = "LEAGUE_SCREEN_ANALYZER_MINIMAP_PROFILES";

    private readonly IReadOnlyDictionary<string, MinimapProfileCatalogEntry> _byId;

    private MinimapProfileCatalog(
        IReadOnlyList<MinimapProfileCatalogEntry> profiles,
        IReadOnlyList<MinimapProfileCatalogError> errors)
    {
        Profiles = profiles;
        Errors = errors;
        _byId = profiles.ToDictionary(profile => profile.Id, StringComparer.Ordinal);
    }

    public IReadOnlyList<MinimapProfileCatalogEntry> Profiles { get; }

    public IReadOnlyList<MinimapProfileCatalogError> Errors { get; }

    public MinimapProfileCatalogEntry Get(string id)
    {
        if (_byId.TryGetValue(id, out MinimapProfileCatalogEntry? entry))
        {
            return entry;
        }

        string detail = Errors.Count == 0
            ? string.Empty
            : $" Discovery errors: {string.Join(" | ", Errors.Select(error => error.Message))}";
        throw new KeyNotFoundException(
            $"Unknown or unavailable minimap profile '{id}'.{detail}");
    }

    public bool TryGet(string id, out MinimapProfileCatalogEntry? entry) =>
        _byId.TryGetValue(id, out entry);

    public MinimapValidationProfile Resolve(string pathOrId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pathOrId);
        if (TryGet(pathOrId, out MinimapProfileCatalogEntry? entry))
        {
            return entry!.Profile;
        }

        string path = Directory.Exists(pathOrId)
            ? Path.Combine(pathOrId, "profile.json")
            : pathOrId;
        if (File.Exists(path))
        {
            return MinimapProfileSerializer.Load(path);
        }

        throw new FileNotFoundException(
            $"Minimap profile '{pathOrId}' is neither an available stable ID nor a readable profile path.",
            path);
    }

    public static MinimapProfileCatalog CreateDefault()
    {
        List<MinimapProfileSearchRoot> roots = [];
        string? profileOverride = Environment.GetEnvironmentVariable(OverrideEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(profileOverride))
        {
            roots.Add(new MinimapProfileSearchRoot(
                Path.GetFullPath(profileOverride),
                MinimapProfileProvenance.DevelopmentOverride));
        }

        string packagedRoot = Path.Combine(
            AppContext.BaseDirectory,
            "fixtures",
            "minimaps");
        roots.Add(new MinimapProfileSearchRoot(
            packagedRoot,
            MinimapProfileProvenance.Packaged));
        roots.Add(new MinimapProfileSearchRoot(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LeagueScreenAnalyzer",
                "profiles",
                "minimaps"),
            MinimapProfileProvenance.UserInstalled));

        if (!ContainsProfiles(packagedRoot))
        {
            string? developmentRoot = FindDevelopmentFixtureRoot();
            if (developmentRoot is not null)
            {
                roots.Add(new MinimapProfileSearchRoot(
                    developmentRoot,
                    MinimapProfileProvenance.DevelopmentOverride));
            }
        }

        return Discover(roots);
    }

    public static MinimapProfileCatalog Discover(
        IEnumerable<MinimapProfileSearchRoot> searchRoots)
    {
        ArgumentNullException.ThrowIfNull(searchRoots);
        Dictionary<string, MinimapProfileCatalogEntry> profiles =
            new(StringComparer.Ordinal)
            {
                [BuiltInMinimapProfiles.LeagueReplayMinimapV1Id] =
                    Entry(
                        BuiltInMinimapProfiles.LeagueReplayMinimapV1,
                        MinimapProfileProvenance.BuiltIn,
                        null)
            };
        HashSet<string> visitedFiles = new(StringComparer.OrdinalIgnoreCase);
        List<MinimapProfileCatalogError> errors = [];
        List<(MinimapValidationProfile Profile, string Path, MinimapProfileProvenance Provenance)>
            discovered = [];

        foreach (MinimapProfileSearchRoot root in searchRoots)
        {
            string fullRoot = Path.GetFullPath(root.Directory);
            if (!Directory.Exists(fullRoot))
            {
                continue;
            }

            foreach (string directory in Directory.EnumerateDirectories(fullRoot)
                         .OrderBy(path => path, StringComparer.Ordinal))
            {
                string profilePath = Path.GetFullPath(Path.Combine(directory, "profile.json"));
                if (!File.Exists(profilePath) || !visitedFiles.Add(profilePath))
                {
                    continue;
                }

                try
                {
                    discovered.Add((
                        MinimapProfileSerializer.Load(profilePath),
                        profilePath,
                        root.Provenance));
                }
                catch (Exception exception) when (
                    exception is IOException or JsonException or InvalidDataException or
                        ArgumentException or UnauthorizedAccessException)
                {
                    errors.Add(new MinimapProfileCatalogError(
                        profilePath,
                        $"Rejected minimap profile '{profilePath}': {exception.Message}"));
                }
            }
        }

        foreach (IGrouping<string, (MinimapValidationProfile Profile, string Path, MinimapProfileProvenance Provenance)> group
                 in discovered.GroupBy(value => value.Profile.Id, StringComparer.Ordinal)
                     .OrderBy(value => value.Key, StringComparer.Ordinal))
        {
            var candidates = group.OrderBy(value => value.Path, StringComparer.Ordinal).ToArray();
            if (candidates.Length > 1)
            {
                profiles.Remove(group.Key);
                foreach (var duplicate in candidates)
                {
                    errors.Add(new MinimapProfileCatalogError(
                        duplicate.Path,
                        $"Duplicate minimap profile ID '{group.Key}' was rejected; no profile may silently replace another."));
                }
                continue;
            }

            var candidate = candidates[0];
            if (profiles.TryGetValue(group.Key, out MinimapProfileCatalogEntry? builtIn))
            {
                bool matchesBuiltIn =
                    builtIn.Provenance == MinimapProfileProvenance.BuiltIn &&
                    builtIn.Profile == candidate.Profile;
                if (!matchesBuiltIn)
                {
                    errors.Add(new MinimapProfileCatalogError(
                        candidate.Path,
                        $"Minimap profile ID '{group.Key}' conflicts with the built-in profile and was rejected; the unchanged built-in profile remains available."));
                    continue;
                }
            }

            profiles[group.Key] = Entry(
                candidate.Profile,
                candidate.Provenance,
                candidate.Path);
        }

        return new MinimapProfileCatalog(
            profiles.Values.OrderBy(value => value.Id, StringComparer.Ordinal).ToArray(),
            errors.OrderBy(value => value.ProfilePath, StringComparer.Ordinal).ToArray());
    }

    private static MinimapProfileCatalogEntry Entry(
        MinimapValidationProfile profile,
        MinimapProfileProvenance provenance,
        string? sourcePath) =>
        new(
            profile.Id,
            profile.DisplayName,
            profile.Version,
            profile.CalibratedForCanonicalRecording,
            provenance,
            sourcePath,
            profile);

    private static bool ContainsProfiles(string root) =>
        Directory.Exists(root) &&
        Directory.EnumerateDirectories(root)
            .Any(directory => File.Exists(Path.Combine(directory, "profile.json")));

    private static string? FindDevelopmentFixtureRoot()
    {
        foreach (string start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            DirectoryInfo? current = new(Path.GetFullPath(start));
            while (current is not null)
            {
                string candidate = Path.Combine(current.FullName, "fixtures", "minimaps");
                if (ContainsProfiles(candidate))
                {
                    return candidate;
                }

                current = current.Parent;
            }
        }

        return null;
    }
}

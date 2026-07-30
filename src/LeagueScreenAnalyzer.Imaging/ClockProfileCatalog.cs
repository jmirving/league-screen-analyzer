using System.Text.Json;
using LeagueScreenAnalyzer.Core.Models;

namespace LeagueScreenAnalyzer.Imaging;

public enum ClockProfileProvenance
{
    BuiltIn,
    Packaged,
    UserInstalled,
    DevelopmentOverride
}

public sealed record ClockProfileCatalogEntry(
    string Id,
    string DisplayName,
    string Family,
    int Version,
    ClockProfileProvenance Provenance,
    int TemplateCount,
    string? SourceManifestPath,
    ClockRecognitionProfile Profile)
{
    public string UiLabel => $"{DisplayName}  [{Id}]  ({TemplateCount} templates)";
}

public sealed record ClockProfileCatalogError(string? ManifestPath, string Message);

public sealed class ClockProfileCatalog
{
    public const string OverrideEnvironmentVariable = "LEAGUE_SCREEN_ANALYZER_CLOCK_PROFILES";

    private readonly IReadOnlyDictionary<string, ClockProfileCatalogEntry> _byId;
    private readonly string _defaultFamily;

    private ClockProfileCatalog(
        IReadOnlyList<ClockProfileCatalogEntry> profiles,
        IReadOnlyList<ClockProfileCatalogError> errors,
        string defaultFamily)
    {
        Profiles = profiles;
        Errors = errors;
        _defaultFamily = defaultFamily;
        _byId = profiles.ToDictionary(profile => profile.Id, StringComparer.Ordinal);
    }

    public IReadOnlyList<ClockProfileCatalogEntry> Profiles { get; }
    public IReadOnlyList<ClockProfileCatalogError> Errors { get; }
    public ClockProfileCatalogEntry DefaultProfile =>
        GetHighestCompatible(_defaultFamily);

    public ClockProfileCatalogEntry Get(string id)
    {
        if (_byId.TryGetValue(id, out ClockProfileCatalogEntry? entry))
        {
            return entry;
        }

        string detail = Errors.Count == 0
            ? string.Empty
            : $" Discovery errors: {string.Join(" | ", Errors.Select(error => error.Message))}";
        throw new KeyNotFoundException($"Unknown or unavailable clock profile '{id}'.{detail}");
    }

    public bool TryGet(string id, out ClockProfileCatalogEntry? entry) =>
        _byId.TryGetValue(id, out entry);

    public ClockProfileCatalogEntry GetHighestCompatible(string family) =>
        ProfileVersionSelection.HighestCompatible(
            Profiles,
            family,
            profile => profile.Family,
            profile => profile.Version);

    public ClockProfileCatalogEntry? SuggestReplacement(string unavailableId) =>
        ProfileVersionKey.TryParseId(unavailableId, out ProfileVersionKey key) &&
        Profiles.Any(profile => string.Equals(
            profile.Family,
            key.Family,
            StringComparison.Ordinal))
            ? GetHighestCompatible(key.Family)
            : null;

    public static ClockProfileCatalog CreateDefault()
    {
        List<ClockProfileSearchRoot> roots = [];
        string? profileOverride = Environment.GetEnvironmentVariable(OverrideEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(profileOverride))
        {
            roots.Add(new ClockProfileSearchRoot(
                Path.GetFullPath(profileOverride),
                ClockProfileProvenance.DevelopmentOverride));
        }

        string packagedRoot = Path.Combine(AppContext.BaseDirectory, "fixtures", "clocks");
        roots.Add(new ClockProfileSearchRoot(packagedRoot, ClockProfileProvenance.Packaged));
        roots.Add(new ClockProfileSearchRoot(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LeagueScreenAnalyzer",
                "profiles"),
            ClockProfileProvenance.UserInstalled));

        if (!ContainsProfileManifests(packagedRoot))
        {
            string? developmentRoot = FindDevelopmentFixtureRoot();
            if (developmentRoot is not null)
            {
                roots.Add(new ClockProfileSearchRoot(
                    developmentRoot,
                    ClockProfileProvenance.DevelopmentOverride));
            }
        }

        return Discover(roots);
    }

    public static ClockProfileCatalog Discover(IEnumerable<ClockProfileSearchRoot> searchRoots)
    {
        ArgumentNullException.ThrowIfNull(searchRoots);
        Dictionary<string, ClockProfileCatalogEntry> profiles = BuiltInClockProfiles.All
            .ToDictionary(
                profile => profile.Id,
                profile =>
                {
                    ProfileVersionKey key =
                        ProfileVersionKey.Parse(profile.Id, profile.Version);
                    return new ClockProfileCatalogEntry(
                        profile.Id,
                        profile.Name,
                        key.Family,
                        profile.Version,
                        ClockProfileProvenance.BuiltIn,
                        0,
                        null,
                        profile);
                },
                StringComparer.Ordinal);
        string defaultFamily = profiles.Values.First().Family;
        HashSet<string> conflictedIds = new(StringComparer.Ordinal);
        HashSet<string> visitedDirectories = new(StringComparer.OrdinalIgnoreCase);
        List<ClockProfileCatalogError> errors = [];
        List<(ClockTemplateManifest Manifest, string Directory, ClockProfileProvenance Provenance)> manifests = [];

        foreach (ClockProfileSearchRoot root in searchRoots)
        {
            string fullRoot = Path.GetFullPath(root.Directory);
            if (!Directory.Exists(fullRoot))
            {
                continue;
            }

            foreach (string directory in Directory.EnumerateDirectories(fullRoot)
                         .OrderBy(path => path, StringComparer.Ordinal))
            {
                string fullDirectory = Path.GetFullPath(directory);
                string manifestPath = Path.Combine(fullDirectory, "manifest.json");
                if (!visitedDirectories.Add(fullDirectory) || !File.Exists(manifestPath) ||
                    !LooksLikeTemplateManifest(manifestPath))
                {
                    continue;
                }

                try
                {
                    ClockTemplateManifest manifest =
                        ClockTemplateProfileLoader.LoadManifest(fullDirectory);
                    _ = ProfileVersionKey.Parse(
                        manifest.ProfileId,
                        manifest.ProfileVersion);
                    manifests.Add((
                        manifest,
                        fullDirectory,
                        root.Provenance));
                }
                catch (Exception exception) when (
                    exception is IOException or JsonException or InvalidDataException or
                        UnauthorizedAccessException)
                {
                    errors.Add(new ClockProfileCatalogError(
                        manifestPath,
                        $"Rejected clock profile manifest '{manifestPath}': {exception.Message}"));
                }
            }
        }

        List<(ClockTemplateManifest Manifest, string Directory, ClockProfileProvenance Provenance)>
            pending = [];
        foreach (IGrouping<string, (ClockTemplateManifest Manifest, string Directory, ClockProfileProvenance Provenance)> group
                 in manifests.GroupBy(item => item.Manifest.ProfileId, StringComparer.Ordinal)
                     .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            (ClockTemplateManifest Manifest, string Directory, ClockProfileProvenance Provenance)[] candidates =
                group.OrderBy(item => item.Directory, StringComparer.Ordinal).ToArray();
            if (candidates.Length > 1)
            {
                conflictedIds.Add(group.Key);
                foreach ((ClockTemplateManifest _, string duplicateDirectory, ClockProfileProvenance _) in candidates)
                {
                    string duplicateManifestPath =
                        Path.Combine(duplicateDirectory, "manifest.json");
                    errors.Add(new ClockProfileCatalogError(
                        duplicateManifestPath,
                        $"Duplicate clock profile ID '{group.Key}' and family/version entry was rejected; no profile may replace another."));
                }
                continue;
            }

            (ClockTemplateManifest manifest, string directory, ClockProfileProvenance provenance) =
                candidates[0];
            string manifestPath = Path.Combine(directory, "manifest.json");
            if (profiles.TryGetValue(manifest.ProfileId, out ClockProfileCatalogEntry? existing))
            {
                bool isPackagedAssetsForBuiltIn =
                    manifest.ProfileId == BuiltInClockProfiles.LeagueReplayV2Id &&
                    manifest.BaseProfileId == BuiltInClockProfiles.LeagueReplayV1Id &&
                    existing.Provenance == ClockProfileProvenance.BuiltIn &&
                    existing.Version == manifest.ProfileVersion &&
                    existing.Profile.Id == manifest.ProfileId;
                if (isPackagedAssetsForBuiltIn)
                {
                    profiles[manifest.ProfileId] = existing with
                    {
                        Provenance = provenance,
                        TemplateCount = manifest.Templates.Count,
                        SourceManifestPath = manifestPath
                    };
                    continue;
                }

                profiles.Remove(manifest.ProfileId);
                conflictedIds.Add(manifest.ProfileId);
                errors.Add(new ClockProfileCatalogError(
                    manifestPath,
                    $"Duplicate clock profile ID '{manifest.ProfileId}' was rejected; it conflicts with '{existing.SourceManifestPath ?? "a built-in profile"}'."));
                continue;
            }

            pending.Add(candidates[0]);
        }

        while (pending.Count > 0)
        {
            int resolved = 0;
            foreach ((ClockTemplateManifest manifest, string directory, ClockProfileProvenance provenance)
                     in pending.ToArray())
            {
                if (!profiles.TryGetValue(
                        manifest.BaseProfileId,
                        out ClockProfileCatalogEntry? baseEntry))
                {
                    continue;
                }

                string manifestPath = Path.Combine(directory, "manifest.json");
                ProfileVersionKey key =
                    ProfileVersionKey.Parse(manifest.ProfileId, manifest.ProfileVersion);
                if (!string.Equals(key.Family, baseEntry.Family, StringComparison.Ordinal))
                {
                    errors.Add(new ClockProfileCatalogError(
                        manifestPath,
                        $"Rejected clock profile '{manifest.ProfileId}': family '{key.Family}' is incompatible with base profile family '{baseEntry.Family}'."));
                    pending.Remove((manifest, directory, provenance));
                    resolved++;
                    continue;
                }
                string displayName = DisplayName(
                    manifest.ProfileId,
                    manifest.ProfileVersion,
                    baseEntry.DisplayName);
                ClockRecognitionProfile profile = (baseEntry.Profile with
                {
                    Id = manifest.ProfileId,
                    Name = displayName,
                    Version = manifest.ProfileVersion
                }).Validate();
                profiles.Add(manifest.ProfileId, new ClockProfileCatalogEntry(
                    profile.Id,
                    displayName,
                    key.Family,
                    profile.Version,
                    provenance,
                    manifest.Templates.Count,
                    manifestPath,
                    profile));
                pending.Remove((manifest, directory, provenance));
                resolved++;
            }

            if (resolved > 0)
            {
                continue;
            }

            foreach ((ClockTemplateManifest manifest, string directory, ClockProfileProvenance _)
                     in pending)
            {
                string manifestPath = Path.Combine(directory, "manifest.json");
                string reason = conflictedIds.Contains(manifest.BaseProfileId)
                    ? "was rejected because its stable ID is duplicated"
                    : "is unavailable or forms a dependency cycle";
                errors.Add(new ClockProfileCatalogError(
                    manifestPath,
                    $"Rejected clock profile '{manifest.ProfileId}': base profile '{manifest.BaseProfileId}' {reason}."));
            }

            pending.Clear();
        }

        foreach (IGrouping<(string Family, int Version), ClockProfileCatalogEntry> duplicate
                 in ProfileVersionSelection.DuplicateFamilyVersions(
                     profiles.Values,
                     profile => profile.Family,
                     profile => profile.Version))
        {
            foreach (ClockProfileCatalogEntry entry in duplicate)
            {
                profiles.Remove(entry.Id);
                errors.Add(new ClockProfileCatalogError(
                    entry.SourceManifestPath,
                    $"Duplicate clock profile family/version '{duplicate.Key.Family}' v{duplicate.Key.Version} was rejected for profile '{entry.Id}'."));
            }
        }

        return new ClockProfileCatalog(
            profiles.Values.OrderBy(profile => profile.Id, StringComparer.Ordinal).ToArray(),
            errors.OrderBy(error => error.ManifestPath, StringComparer.Ordinal).ToArray(),
            defaultFamily);
    }

    private static string DisplayName(string id, int version, string baseName) => id switch
    {
        BuiltInClockProfiles.LeagueReplayV1Id => "League Replay HUD — v1 synthetic",
        BuiltInClockProfiles.LeagueReplayV2Id => "League Replay HUD — v2 real calibrated",
        _ => $"{baseName} — v{version} generated"
    };

    private static bool LooksLikeTemplateManifest(string path)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            return document.RootElement.TryGetProperty("schemaVersion", out _);
        }
        catch (JsonException)
        {
            return true;
        }
    }

    private static bool ContainsProfileManifests(string root) =>
        Directory.Exists(root) &&
        Directory.EnumerateDirectories(root).Any(directory =>
            File.Exists(Path.Combine(directory, "manifest.json")) &&
            LooksLikeTemplateManifest(Path.Combine(directory, "manifest.json")));

    private static string? FindDevelopmentFixtureRoot()
    {
        foreach (string start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            DirectoryInfo? current = new(Path.GetFullPath(start));
            while (current is not null)
            {
                string candidate = Path.Combine(current.FullName, "fixtures", "clocks");
                if (ContainsProfileManifests(candidate))
                {
                    return candidate;
                }

                current = current.Parent;
            }
        }

        return null;
    }
}

public sealed record ClockProfileSearchRoot(
    string Directory,
    ClockProfileProvenance Provenance);

using System.Text.Json;
using System.Text.Json.Serialization;
using LeagueScreenAnalyzer.Core.Models;

namespace LeagueScreenAnalyzer.Storage;

public interface ICaptureLayoutStore
{
    Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(
        CaptureLayout layout,
        bool overwrite,
        CancellationToken cancellationToken = default);

    Task<CaptureLayout> LoadAsync(string name, CancellationToken cancellationToken = default);

    Task DeleteAsync(string name, CancellationToken cancellationToken = default);
}

public sealed class CaptureLayoutException(string message, Exception? innerException = null)
    : Exception(message, innerException);

public sealed class JsonCaptureLayoutStore : ICaptureLayoutStore
{
    public const int CurrentSchemaVersion = 1;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    private readonly string _directory;

    public JsonCaptureLayoutStore(string? directory = null)
    {
        _directory = Path.GetFullPath(directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LeagueScreenAnalyzer",
            "CaptureLayouts"));
    }

    public string DirectoryPath => _directory;

    public Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Directory.Exists(_directory))
        {
            return Task.FromResult<IReadOnlyList<string>>([]);
        }

        IReadOnlyList<string> names = Directory
            .EnumerateFiles(_directory, "*.json", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileNameWithoutExtension)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray()!;
        return Task.FromResult(names);
    }

    public async Task SaveAsync(
        CaptureLayout layout,
        bool overwrite,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(layout);
        string path = GetPath(layout.Name);
        Directory.CreateDirectory(_directory);
        if (!overwrite && File.Exists(path))
        {
            throw new CaptureLayoutException(
                $"A capture layout named '{layout.Name}' already exists. Choose overwrite explicitly.");
        }

        LayoutDocument document = new()
        {
            SchemaVersion = CurrentSchemaVersion,
            Name = layout.Name,
            SourceAspectRatio = layout.SourceAspectRatio,
            ClockRegion = RegionDocument.From(layout.ClockRegion),
            MinimapRegion = RegionDocument.From(layout.MinimapRegion)
        };
        string temporaryPath = Path.Combine(
            _directory,
            $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (FileStream stream = new(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    document,
                    SerializerOptions,
                    cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, path, overwrite);
        }
        catch (CaptureLayoutException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new CaptureLayoutException(
                $"Could not save capture layout '{layout.Name}': {exception.Message}",
                exception);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public async Task<CaptureLayout> LoadAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        string path = GetPath(name);
        if (!File.Exists(path))
        {
            throw new CaptureLayoutException($"Capture layout '{name}' was not found.");
        }

        try
        {
            await using FileStream stream = File.OpenRead(path);
            LayoutDocument? document = await JsonSerializer.DeserializeAsync<LayoutDocument>(
                stream,
                SerializerOptions,
                cancellationToken);
            if (document is null)
            {
                throw new CaptureLayoutException($"Capture layout '{name}' is empty.");
            }

            return Validate(document, name);
        }
        catch (CaptureLayoutException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw new CaptureLayoutException(
                $"Capture layout '{name}' contains malformed or incomplete JSON: {exception.Message}",
                exception);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new CaptureLayoutException(
                $"Could not load capture layout '{name}': {exception.Message}",
                exception);
        }
    }

    public Task DeleteAsync(string name, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string path = GetPath(name);
        if (!File.Exists(path))
        {
            throw new CaptureLayoutException($"Capture layout '{name}' was not found.");
        }

        try
        {
            File.Delete(path);
            return Task.CompletedTask;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new CaptureLayoutException(
                $"Could not delete capture layout '{name}': {exception.Message}",
                exception);
        }
    }

    private static CaptureLayout Validate(LayoutDocument document, string requestedName)
    {
        if (document.SchemaVersion != CurrentSchemaVersion)
        {
            throw new CaptureLayoutException(
                $"Capture layout '{requestedName}' uses unsupported schema version " +
                $"{document.SchemaVersion}; expected {CurrentSchemaVersion}.");
        }

        if (string.IsNullOrWhiteSpace(document.Name))
        {
            throw new CaptureLayoutException($"Capture layout '{requestedName}' is missing its name.");
        }

        if (!string.Equals(document.Name, requestedName, StringComparison.OrdinalIgnoreCase))
        {
            throw new CaptureLayoutException(
                $"Capture layout file '{requestedName}' declares the different name '{document.Name}'.");
        }

        return new CaptureLayout(
            document.Name,
            document.ClockRegion?.ToRegion("clockRegion")
                ?? throw new CaptureLayoutException(
                    $"Capture layout '{requestedName}' is missing clockRegion."),
            document.MinimapRegion?.ToRegion("minimapRegion")
                ?? throw new CaptureLayoutException(
                    $"Capture layout '{requestedName}' is missing minimapRegion."),
            document.SourceAspectRatio);
    }

    private string GetPath(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name is "." or ".."
            || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || name.Contains(Path.DirectorySeparatorChar)
            || name.Contains(Path.AltDirectorySeparatorChar))
        {
            throw new CaptureLayoutException(
                "Layout names cannot contain path separators or invalid filename characters.");
        }

        return Path.Combine(_directory, $"{name}.json");
    }

    private sealed class LayoutDocument
    {
        public int SchemaVersion { get; init; }

        public string? Name { get; init; }

        public double? SourceAspectRatio { get; init; }

        public RegionDocument? ClockRegion { get; init; }

        public RegionDocument? MinimapRegion { get; init; }
    }

    private sealed class RegionDocument
    {
        public double? X { get; init; }

        public double? Y { get; init; }

        public double? Width { get; init; }

        public double? Height { get; init; }

        public static RegionDocument From(NormalizedRegion region) => new()
        {
            X = region.X,
            Y = region.Y,
            Width = region.Width,
            Height = region.Height
        };

        public NormalizedRegion ToRegion(string propertyName)
        {
            if (X is null || Y is null || Width is null || Height is null)
            {
                throw new CaptureLayoutException(
                    $"{propertyName} must include numeric x, y, width, and height fields.");
            }

            try
            {
                return new NormalizedRegion(X.Value, Y.Value, Width.Value, Height.Value);
            }
            catch (ArgumentOutOfRangeException exception)
            {
                throw new CaptureLayoutException(
                    $"{propertyName} is outside normalized bounds: {exception.Message}",
                    exception);
            }
        }
    }
}

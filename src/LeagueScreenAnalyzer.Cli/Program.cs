using LeagueScreenAnalyzer.Imaging;

namespace LeagueScreenAnalyzer.Cli;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.FirstOrDefault() == "analyze-clock-diagnostics")
            {
                Dictionary<string, string> values = ParseNamedArguments(args.Skip(1).ToArray());
                ClockCalibrationAnalysisReport report = await new ClockCalibrationService()
                    .AnalyzeAsync(
                        Required(values, "--profile"),
                        Required(values, "--diagnostics"),
                        Required(values, "--output"))
                    .ConfigureAwait(false);
                Console.WriteLine(
                    $"Clock calibration analysis: {report.SampleCount} labeled samples. " +
                    $"Unsupported digits: {string.Join(", ", report.Coverage.UnsupportedDigits)}.");
                return 0;
            }

            if (args.FirstOrDefault() == "build-clock-profile")
            {
                Dictionary<string, string> values = ParseNamedArguments(args.Skip(1).ToArray());
                ClockTemplateManifest manifest = await new ClockCalibrationService()
                    .BuildProfileAsync(
                        Required(values, "--base-profile"),
                        Required(values, "--profile-id"),
                        Required(values, "--diagnostics"),
                        Required(values, "--output-profile"))
                    .ConfigureAwait(false);
                Console.WriteLine(
                    $"Built {manifest.ProfileId} with {manifest.Templates.Count} provenance-tracked templates.");
                return 0;
            }

            if (args.FirstOrDefault() == "evaluate-clock")
            {
                Dictionary<string, string> values = ParseNamedArguments(args.Skip(1).ToArray());
                string profile = Required(values, "--profile");
                string output = Required(values, "--output");
                bool hasManifest = values.TryGetValue("--manifest", out string? manifest);
                bool hasDiagnostics = values.TryGetValue("--diagnostics", out string? diagnostics);
                if (hasManifest == hasDiagnostics)
                {
                    throw new ArgumentException(
                        "Specify exactly one of --manifest or --diagnostics.");
                }

                ClockEvaluationService clockEvaluationService = new();
                ClockEvaluationReport report = hasManifest
                    ? await clockEvaluationService.EvaluateAsync(
                        profile,
                        manifest!,
                        output).ConfigureAwait(false)
                    : await clockEvaluationService.EvaluateDiagnosticBundlesAsync(
                        profile,
                        diagnostics!,
                        output).ConfigureAwait(false);
                Console.WriteLine(
                    $"Clock evaluation: {report.TotalSamples} samples, " +
                    $"{report.FalseAccepts} false accepts, {report.FalseRejects} false rejects. " +
                    $"Report: {Path.GetFullPath(output)}");
                return report.FalseAccepts == 0 ? 0 : 1;
            }

            FixtureCommandOptions options = FixtureCommandOptions.Parse(args);
            FixtureProcessingService service = new();
            await service.ProcessAsync(options.SourcePath, options.OutputPath).ConfigureAwait(false);
            Console.WriteLine($"Fixture processed successfully. Artifacts: {Path.GetFullPath(options.OutputPath)}");
            return 0;
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine($"Argument error: {exception.Message}");
            Console.Error.WriteLine(
                "Usage:\n" +
                "  process-fixture --source <fixture-manifest.json> --output <artifact-directory>\n" +
                "  analyze-clock-diagnostics --profile <id> --diagnostics <directory> --output <directory>\n" +
                "  build-clock-profile --base-profile <id> --profile-id <id> --diagnostics <directory> --output-profile <directory>\n" +
                "  evaluate-clock --profile <id> --manifest <manifest.json> --output <directory>\n" +
                "  evaluate-clock --profile <id> --diagnostics <clock-samples-directory> --output <directory>");
            return 2;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Processing failed: {exception.Message}");
            return 1;
        }
    }

    private static Dictionary<string, string> ParseNamedArguments(string[] args)
    {
        Dictionary<string, string> values = new(StringComparer.Ordinal);
        for (int i = 0; i < args.Length; i += 2)
        {
            if (i + 1 >= args.Length || !args[i].StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException("Clock evaluation arguments must be --name value pairs.");
            }

            values[args[i]] = args[i + 1];
        }

        return values;
    }

    private static string Required(IReadOnlyDictionary<string, string> values, string key) =>
        values.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ArgumentException($"Missing required argument {key}.");
}

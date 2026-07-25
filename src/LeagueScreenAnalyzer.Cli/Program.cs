namespace LeagueScreenAnalyzer.Cli;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
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
                "Usage: process-fixture --source <fixture-manifest.json> --output <artifact-directory>");
            return 2;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Processing failed: {exception.Message}");
            return 1;
        }
    }
}

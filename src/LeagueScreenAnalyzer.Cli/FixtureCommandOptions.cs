namespace LeagueScreenAnalyzer.Cli;

public sealed record FixtureCommandOptions(string SourcePath, string OutputPath)
{
    public static FixtureCommandOptions Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Count == 0 || !string.Equals(args[0], "process-fixture", StringComparison.Ordinal))
        {
            throw new ArgumentException("The required command is 'process-fixture'.", nameof(args));
        }

        string? source = null;
        string? output = null;

        for (int index = 1; index < args.Count; index += 2)
        {
            if (index + 1 >= args.Count)
            {
                throw new ArgumentException($"Option '{args[index]}' requires a value.", nameof(args));
            }

            string option = args[index];
            string value = args[index + 1];
            ArgumentException.ThrowIfNullOrWhiteSpace(value);

            switch (option)
            {
                case "--source" when source is null:
                    source = value;
                    break;
                case "--output" when output is null:
                    output = value;
                    break;
                case "--source":
                case "--output":
                    throw new ArgumentException($"Option '{option}' was specified more than once.", nameof(args));
                default:
                    throw new ArgumentException($"Unknown option '{option}'.", nameof(args));
            }
        }

        if (source is null)
        {
            throw new ArgumentException("The --source option is required.", nameof(args));
        }

        if (output is null)
        {
            throw new ArgumentException("The --output option is required.", nameof(args));
        }

        return new FixtureCommandOptions(source, output);
    }
}

namespace LeagueScreenAnalyzer.Core.Regions;

public interface ISourceAspectRatioCompatibility
{
    bool IsMaterialMismatch(double expectedAspectRatio, double actualAspectRatio);

    double CalculateRelativeDifference(double expectedAspectRatio, double actualAspectRatio);
}

public sealed class SourceAspectRatioCompatibility(double threshold = 0.02)
    : ISourceAspectRatioCompatibility
{
    public double Threshold { get; } =
        double.IsFinite(threshold) && threshold >= 0
            ? threshold
            : throw new ArgumentOutOfRangeException(nameof(threshold));

    public bool IsMaterialMismatch(double expectedAspectRatio, double actualAspectRatio) =>
        CalculateRelativeDifference(expectedAspectRatio, actualAspectRatio) > Threshold;

    public double CalculateRelativeDifference(double expectedAspectRatio, double actualAspectRatio)
    {
        Validate(expectedAspectRatio, nameof(expectedAspectRatio));
        Validate(actualAspectRatio, nameof(actualAspectRatio));
        return Math.Abs(actualAspectRatio - expectedAspectRatio) / expectedAspectRatio;
    }

    private static void Validate(double value, string parameterName)
    {
        if (!double.IsFinite(value) || value <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}

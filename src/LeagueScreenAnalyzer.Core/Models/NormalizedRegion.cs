namespace LeagueScreenAnalyzer.Core.Models;

public sealed record NormalizedRegion
{
    public NormalizedRegion(double x, double y, double width, double height)
    {
        ValidateValue(x, nameof(x));
        ValidateValue(y, nameof(y));
        ValidateValue(width, nameof(width));
        ValidateValue(height, nameof(height));

        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), width, "Width must be greater than zero.");
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), height, "Height must be greater than zero.");
        }

        if (x + width > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(width), width, "X plus width must not exceed 1.");
        }

        if (y + height > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(height), height, "Y plus height must not exceed 1.");
        }

        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public double X { get; }

    public double Y { get; }

    public double Width { get; }

    public double Height { get; }

    private static void ValidateValue(double value, string parameterName)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(parameterName, value, $"{parameterName} must be finite.");
        }

        if (value < 0 || value > 1)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, $"{parameterName} must be between 0 and 1.");
        }
    }
}

using System.Windows;
using System.Windows.Controls;

namespace LeagueScreenAnalyzer.App;

public sealed class AspectRatioDecorator : Decorator
{
    public static readonly DependencyProperty AspectRatioProperty =
        DependencyProperty.Register(
            nameof(AspectRatio),
            typeof(double),
            typeof(AspectRatioDecorator),
            new FrameworkPropertyMetadata(
                1d,
                FrameworkPropertyMetadataOptions.AffectsMeasure |
                FrameworkPropertyMetadataOptions.AffectsArrange),
            value => value is double ratio && double.IsFinite(ratio) && ratio > 0);

    public double AspectRatio
    {
        get => (double)GetValue(AspectRatioProperty);
        set => SetValue(AspectRatioProperty, value);
    }

    protected override Size MeasureOverride(Size constraint)
    {
        if (Child is null)
        {
            return default;
        }

        Size available = Fit(constraint, AspectRatio);
        Child.Measure(available);
        return new Size(
            double.IsFinite(constraint.Width) ? constraint.Width : available.Width,
            double.IsFinite(constraint.Height) ? constraint.Height : available.Height);
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        if (Child is null)
        {
            return arrangeSize;
        }

        Size fitted = Fit(arrangeSize, AspectRatio);
        Child.Arrange(new Rect(
            (arrangeSize.Width - fitted.Width) / 2,
            (arrangeSize.Height - fitted.Height) / 2,
            fitted.Width,
            fitted.Height));
        return arrangeSize;
    }

    private static Size Fit(Size available, double ratio)
    {
        double width = double.IsFinite(available.Width) ? Math.Max(0, available.Width) : 0;
        double height = double.IsFinite(available.Height) ? Math.Max(0, available.Height) : 0;
        if (!double.IsFinite(available.Width) && double.IsFinite(available.Height))
        {
            width = height * ratio;
        }
        else if (double.IsFinite(available.Width) && !double.IsFinite(available.Height))
        {
            height = width / ratio;
        }
        else if (!double.IsFinite(available.Width) && !double.IsFinite(available.Height))
        {
            return default;
        }

        if (height > 0 && width / height > ratio)
        {
            width = height * ratio;
        }
        else if (ratio > 0)
        {
            height = width / ratio;
        }

        return new Size(width, height);
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace LeagueScreenAnalyzer.App;

public partial class CropPreviewControl : UserControl
{
    public static readonly DependencyProperty ImageSourceProperty = DependencyProperty.Register(
        nameof(ImageSource),
        typeof(ImageSource),
        typeof(CropPreviewControl),
        new PropertyMetadata(null));

    public CropPreviewControl()
    {
        InitializeComponent();
    }

    public ImageSource? ImageSource
    {
        get => (ImageSource?)GetValue(ImageSourceProperty);
        set => SetValue(ImageSourceProperty, value);
    }
}

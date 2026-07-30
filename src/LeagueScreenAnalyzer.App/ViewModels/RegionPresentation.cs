using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using LeagueScreenAnalyzer.Core.Models;

namespace LeagueScreenAnalyzer.App.ViewModels;

public sealed class RegionOverlayViewModel : INotifyPropertyChanged
{
    private double _left;
    private double _top;
    private double _width;
    private double _height;
    private bool _isVisible;
    private bool _isSelected;

    public RegionOverlayViewModel(RegionType regionType)
    {
        RegionType = regionType;
        Label = regionType == RegionType.Clock ? "CLOCK" : "MINIMAP";
        BorderBrush = regionType == RegionType.Clock ? Brushes.Gold : Brushes.DeepSkyBlue;
    }

    public RegionType RegionType { get; }

    public string Label { get; }

    public Brush BorderBrush { get; }

    public bool AreEdgeHandlesVisible => RegionType == RegionType.Clock;

    public double Left { get => _left; private set => Set(ref _left, value); }

    public double Top { get => _top; private set => Set(ref _top, value); }

    public double Width { get => _width; private set => Set(ref _width, value); }

    public double Height { get => _height; private set => Set(ref _height, value); }

    public bool IsVisible { get => _isVisible; private set => Set(ref _isVisible, value); }

    public bool IsSelected { get => _isSelected; private set => Set(ref _isSelected, value); }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Update(double left, double top, double width, double height, bool selected)
    {
        Left = left;
        Top = top;
        Width = width;
        Height = height;
        IsVisible = true;
        IsSelected = selected;
    }

    public void Hide()
    {
        IsVisible = false;
        IsSelected = false;
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class CropPreviewViewModel(string name) : INotifyPropertyChanged
{
    private ImageSource? _image;
    private string _dimensions = "Not configured";
    private string _coordinates = "Not configured";

    public string Name { get; } = name;

    public ImageSource? Image { get => _image; set => Set(ref _image, value); }

    public string Dimensions { get => _dimensions; set => Set(ref _dimensions, value); }

    public string Coordinates { get => _coordinates; set => Set(ref _coordinates, value); }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

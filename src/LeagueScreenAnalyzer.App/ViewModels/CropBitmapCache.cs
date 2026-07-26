using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LeagueScreenAnalyzer.App.ViewModels;

internal sealed class CropBitmapCache
{
    private WriteableBitmap? _bitmap;

    public WriteableBitmap GetOrCreate(int pixelWidth, int pixelHeight)
    {
        if (_bitmap is null
            || _bitmap.PixelWidth != pixelWidth
            || _bitmap.PixelHeight != pixelHeight)
        {
            _bitmap = new WriteableBitmap(
                pixelWidth,
                pixelHeight,
                96,
                96,
                PixelFormats.Bgra32,
                null);
        }

        return _bitmap;
    }
}

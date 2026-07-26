using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LeagueScreenAnalyzer.App;
using LeagueScreenAnalyzer.App.ViewModels;

namespace LeagueScreenAnalyzer.Tests;

public sealed class CropPreviewTests
{
    [Theory]
    [InlineData(311, 305)]
    [InlineData(420, 84)]
    public void LayoutResize_ShrinksAndExpandsWithoutChangingSourceBitmap(
        int pixelWidth,
        int pixelHeight) =>
        RunOnSta(() =>
        {
            WriteableBitmap bitmap = CreateBitmap(pixelWidth, pixelHeight);
            CropPreviewControl preview = new()
            {
                ImageSource = bitmap
            };

            Size original = ArrangeAndGetRenderedImageSize(preview, 420, 220);
            Size smaller = default;
            Size restored = default;
            for (int iteration = 0; iteration < 3; iteration++)
            {
                smaller = ArrangeAndGetRenderedImageSize(preview, 140, 90);
                restored = ArrangeAndGetRenderedImageSize(preview, 420, 220);
            }

            Assert.Same(bitmap, preview.ImageSource);
            Assert.Equal(pixelWidth, bitmap.PixelWidth);
            Assert.Equal(pixelHeight, bitmap.PixelHeight);
            Assert.True(smaller.Width < original.Width);
            Assert.True(smaller.Height < original.Height);
            Assert.Equal(original.Width, restored.Width, 6);
            Assert.Equal(original.Height, restored.Height, 6);
            Assert.Equal((double)pixelWidth / pixelHeight, restored.Width / restored.Height, 6);
        });

    [Fact]
    public void BitmapCache_ReusesActualCropDimensionsAndRecreatesChangedDimensions() =>
        RunOnSta(() =>
        {
            CropBitmapCache cache = new();

            WriteableBitmap original = cache.GetOrCreate(311, 305);
            WriteableBitmap afterPresentationResize = cache.GetOrCreate(311, 305);
            WriteableBitmap afterSourceResize = cache.GetOrCreate(415, 407);

            Assert.Same(original, afterPresentationResize);
            Assert.NotSame(original, afterSourceResize);
            Assert.Equal(415, afterSourceResize.PixelWidth);
            Assert.Equal(407, afterSourceResize.PixelHeight);
        });

    private static Size ArrangeAndGetRenderedImageSize(
        CropPreviewControl preview,
        double width,
        double height)
    {
        preview.Measure(new Size(width, height));
        preview.Arrange(new Rect(0, 0, width, height));
        preview.UpdateLayout();

        Image image = FindDescendant<Image>(preview);
        GeneralTransform transform = image.TransformToAncestor(preview);
        return transform.TransformBounds(new Rect(image.RenderSize)).Size;
    }

    private static T FindDescendant<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
            {
                return match;
            }

            T? descendant = FindDescendant<T>(child);
            if (descendant is not null)
            {
                return descendant;
            }
        }

        return null!;
    }

    private static WriteableBitmap CreateBitmap(int width, int height) =>
        new(width, height, 96, 96, PixelFormats.Bgra32, null);

    private static void RunOnSta(Action action)
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}

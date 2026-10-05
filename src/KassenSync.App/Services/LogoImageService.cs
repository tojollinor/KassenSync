using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace KassenSync.App.Services;

public static class LogoImageService
{
    private static ImageSource? _cached;

    public static ImageSource? GetLargestIconFrame()
    {
        if (_cached is not null)
            return _cached;

        try
        {
            var uri = new Uri(
                "pack://application:,,,/OrdnerSync.App;component/Assets/OrdnerSync.ico",
                UriKind.Absolute);

            var decoder = BitmapDecoder.Create(
                uri,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);

            var frame = decoder.Frames
                .OrderByDescending(x => x.PixelWidth * x.PixelHeight)
                .ThenByDescending(x => x.PixelWidth)
                .FirstOrDefault();

            if (frame is null)
                return null;

            frame.Freeze();
            _cached = frame;
            return _cached;
        }
        catch
        {
            return null;
        }
    }
}

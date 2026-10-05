using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace KassenSync.App.Services;

public static class LogoImageService
{
    private static readonly Dictionary<int, ImageSource?> Cache = new();

    public static ImageSource? GetBestIconFrame(int desiredPixels)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(desiredPixels, out var cached))
                return cached;

            try
            {
                var uri = new Uri(
                    "pack://application:,,,/OrdnerSync.App;component/Assets/OrdnerSync.ico",
                    UriKind.Absolute);

                var decoder = BitmapDecoder.Create(
                    uri,
                    BitmapCreateOptions.PreservePixelFormat,
                    BitmapCacheOption.OnLoad);

                var allFrames = decoder.Frames
                    .Where(x => x.PixelWidth > 0 && x.PixelHeight > 0)
                    .ToArray();

                if (allFrames.Length == 0)
                {
                    Cache[desiredPixels] = null;
                    return null;
                }

                // Die größte ICO-Ebene erzeugt auf einzelnen Windows/WPF-Systemen
                // sichtbare Farbartefakte. Für die GUI bevorzugen wir deshalb
                // die passendste Ebene bis maximal 128 px.
                var safeFrames = allFrames
                    .Where(x => x.PixelWidth <= 128 && x.PixelHeight <= 128)
                    .ToArray();

                var pool = safeFrames.Length > 0 ? safeFrames : allFrames;

                var frame = pool
                    .OrderBy(x => x.PixelWidth < desiredPixels ? 1 : 0)
                    .ThenBy(x => Math.Abs(x.PixelWidth - desiredPixels))
                    .ThenByDescending(x => x.PixelWidth)
                    .First();

                frame.Freeze();
                Cache[desiredPixels] = frame;
                return frame;
            }
            catch
            {
                Cache[desiredPixels] = null;
                return null;
            }
        }
    }
}

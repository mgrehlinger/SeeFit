using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SeeFit.Fits;

public sealed class FitsImage
{
    public required IReadOnlyList<FitsKeyword> Keywords { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required int BitPix { get; init; }
    public required double[,] PixelData { get; init; }
    public required BitmapSource Preview { get; init; }

    public string? GetKeywordValue(string keyword) =>
        Keywords.FirstOrDefault(k =>
            string.Equals(k.Keyword, keyword, StringComparison.OrdinalIgnoreCase))?.Value;

    public static BitmapSource CreatePreview(double[,] pixels, int width, int height)
    {
        var flat = new double[width * height];
        var index = 0;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                flat[index++] = pixels[y, x];
            }
        }

        Array.Sort(flat);
        var lo = Percentile(flat, 0.01);
        var hi = Percentile(flat, 0.99);
        if (Math.Abs(hi - lo) < double.Epsilon)
        {
            lo = flat[0];
            hi = flat[^1];
            if (Math.Abs(hi - lo) < double.Epsilon)
            {
                hi = lo + 1;
            }
        }

        var stride = width;
        var buffer = new byte[width * height];
        var range = hi - lo;

        for (var y = 0; y < height; y++)
        {
            // FITS stores the first row as the bottom of the image.
            var srcY = height - 1 - y;
            for (var x = 0; x < width; x++)
            {
                var normalized = (pixels[srcY, x] - lo) / range;
                normalized = Math.Clamp(normalized, 0, 1);
                buffer[y * stride + x] = (byte)Math.Round(normalized * 255.0);
            }
        }

        var bitmap = BitmapSource.Create(
            width,
            height,
            96,
            96,
            PixelFormats.Gray8,
            null,
            buffer,
            stride);

        bitmap.Freeze();
        return bitmap;
    }

    private static double Percentile(double[] sorted, double percentile)
    {
        if (sorted.Length == 0)
        {
            return 0;
        }

        var position = percentile * (sorted.Length - 1);
        var lower = (int)Math.Floor(position);
        var upper = (int)Math.Ceiling(position);
        if (lower == upper)
        {
            return sorted[lower];
        }

        var weight = position - lower;
        return sorted[lower] * (1 - weight) + sorted[upper] * weight;
    }
}

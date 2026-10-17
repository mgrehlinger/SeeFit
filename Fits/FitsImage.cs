using System.Globalization;
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

    public string? GetKeywordValue(string keyword) =>
        Keywords.FirstOrDefault(k =>
            string.Equals(k.Keyword, keyword, StringComparison.OrdinalIgnoreCase))?.Value;

    public BayerPattern? HeaderBayerPattern =>
        BayerPatternParser.TryParse(GetKeywordValue("BAYERPAT"));

    public int HeaderBayerXOffset => GetIntKeyword("XBAYROFF", 0);

    public int HeaderBayerYOffset => GetIntKeyword("YBAYROFF", 0);

    public BitmapSource CreatePreview(bool debayer, BayerPattern pattern, ScnrOptions? scnr = null)
    {
        if (!debayer)
        {
            return CreateGrayPreview(PixelData, Width, Height);
        }

        var (r, g, b) = Debayer.Bilinear(
            PixelData,
            Width,
            Height,
            pattern,
            HeaderBayerXOffset,
            HeaderBayerYOffset);

        if (scnr is { Enabled: true })
        {
            Scnr.Apply(r, g, b, Width, Height, scnr);
        }

        return CreateColorPreview(r, g, b, Width, Height);
    }

    public static BitmapSource CreateGrayPreview(double[,] pixels, int width, int height)
    {
        GetStretchLimits(pixels, width, height, out var lo, out var hi);
        var range = hi - lo;
        var stride = width;
        var buffer = new byte[width * height];

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

    public static BitmapSource CreateColorPreview(
        double[,] red,
        double[,] green,
        double[,] blue,
        int width,
        int height)
    {
        GetStretchLimits(green, width, height, out var lo, out var hi);
        var range = hi - lo;
        var stride = width * 3;
        var buffer = new byte[stride * height];

        for (var y = 0; y < height; y++)
        {
            var srcY = height - 1 - y;
            var row = y * stride;
            for (var x = 0; x < width; x++)
            {
                var i = row + x * 3;
                buffer[i] = ToByte(red[srcY, x], lo, range);
                buffer[i + 1] = ToByte(green[srcY, x], lo, range);
                buffer[i + 2] = ToByte(blue[srcY, x], lo, range);
            }
        }

        var bitmap = BitmapSource.Create(
            width,
            height,
            96,
            96,
            PixelFormats.Rgb24,
            null,
            buffer,
            stride);

        bitmap.Freeze();
        return bitmap;
    }

    private static byte ToByte(double value, double lo, double range)
    {
        var normalized = Math.Clamp((value - lo) / range, 0, 1);
        return (byte)Math.Round(normalized * 255.0);
    }

    private static void GetStretchLimits(
        double[,] pixels,
        int width,
        int height,
        out double lo,
        out double hi)
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
        lo = Percentile(flat, 0.01);
        hi = Percentile(flat, 0.99);
        if (Math.Abs(hi - lo) < double.Epsilon)
        {
            lo = flat[0];
            hi = flat[^1];
            if (Math.Abs(hi - lo) < double.Epsilon)
            {
                hi = lo + 1;
            }
        }
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

    private int GetIntKeyword(string name, int fallback)
    {
        var value = GetKeywordValue(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;
    }
}

namespace SeeFit.Fits;

public static class Debayer
{
    /// <summary>
    /// Bilinear demosaic of a CFA mosaic into planar RGB (height, width) per channel.
    /// </summary>
    public static (double[,] R, double[,] G, double[,] B) Bilinear(
        double[,] mosaic,
        int width,
        int height,
        BayerPattern pattern,
        int xOffset = 0,
        int yOffset = 0)
    {
        var r = new double[height, width];
        var g = new double[height, width];
        var b = new double[height, width];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var channel = BayerPatternParser.ChannelAt(pattern, x, y, xOffset, yOffset);
                var value = mosaic[y, x];

                switch (channel)
                {
                    case 0: // R
                        r[y, x] = value;
                        g[y, x] = AverageOrthogonal(mosaic, width, height, x, y);
                        b[y, x] = AverageDiagonal(mosaic, width, height, x, y);
                        break;
                    case 2: // B
                        b[y, x] = value;
                        g[y, x] = AverageOrthogonal(mosaic, width, height, x, y);
                        r[y, x] = AverageDiagonal(mosaic, width, height, x, y);
                        break;
                    default: // G
                        g[y, x] = value;
                        // On a green site, R and B come from horizontal or vertical neighbors
                        // depending on which row of the 2×2 tile we are on.
                        InterpolateGreenSite(
                            mosaic, width, height, pattern, xOffset, yOffset, x, y,
                            out r[y, x], out b[y, x]);
                        break;
                }
            }
        }

        return (r, g, b);
    }

    private static void InterpolateGreenSite(
        double[,] mosaic,
        int width,
        int height,
        BayerPattern pattern,
        int xOffset,
        int yOffset,
        int x,
        int y,
        out double red,
        out double blue)
    {
        // Neighbor to the left/right shares the row; up/down share the column.
        // One pair is R, the other is B — determined by the Bayer layout.
        var leftChannel = x > 0
            ? BayerPatternParser.ChannelAt(pattern, x - 1, y, xOffset, yOffset)
            : BayerPatternParser.ChannelAt(pattern, x + 1, y, xOffset, yOffset);
        var upChannel = y > 0
            ? BayerPatternParser.ChannelAt(pattern, x, y - 1, xOffset, yOffset)
            : BayerPatternParser.ChannelAt(pattern, x, y + 1, xOffset, yOffset);

        var horizontal = AverageHorizontal(mosaic, width, height, x, y);
        var vertical = AverageVertical(mosaic, width, height, x, y);

        if (leftChannel == 0)
        {
            red = horizontal;
            blue = vertical;
        }
        else if (leftChannel == 2)
        {
            blue = horizontal;
            red = vertical;
        }
        else if (upChannel == 0)
        {
            red = vertical;
            blue = horizontal;
        }
        else
        {
            blue = vertical;
            red = horizontal;
        }
    }

    private static double AverageOrthogonal(double[,] m, int width, int height, int x, int y)
    {
        double sum = 0;
        var count = 0;
        Accumulate(m, width, height, x - 1, y, ref sum, ref count);
        Accumulate(m, width, height, x + 1, y, ref sum, ref count);
        Accumulate(m, width, height, x, y - 1, ref sum, ref count);
        Accumulate(m, width, height, x, y + 1, ref sum, ref count);
        return count > 0 ? sum / count : m[y, x];
    }

    private static double AverageDiagonal(double[,] m, int width, int height, int x, int y)
    {
        double sum = 0;
        var count = 0;
        Accumulate(m, width, height, x - 1, y - 1, ref sum, ref count);
        Accumulate(m, width, height, x + 1, y - 1, ref sum, ref count);
        Accumulate(m, width, height, x - 1, y + 1, ref sum, ref count);
        Accumulate(m, width, height, x + 1, y + 1, ref sum, ref count);
        return count > 0 ? sum / count : m[y, x];
    }

    private static double AverageHorizontal(double[,] m, int width, int height, int x, int y)
    {
        double sum = 0;
        var count = 0;
        Accumulate(m, width, height, x - 1, y, ref sum, ref count);
        Accumulate(m, width, height, x + 1, y, ref sum, ref count);
        return count > 0 ? sum / count : m[y, x];
    }

    private static double AverageVertical(double[,] m, int width, int height, int x, int y)
    {
        double sum = 0;
        var count = 0;
        Accumulate(m, width, height, x, y - 1, ref sum, ref count);
        Accumulate(m, width, height, x, y + 1, ref sum, ref count);
        return count > 0 ? sum / count : m[y, x];
    }

    private static void Accumulate(
        double[,] m,
        int width,
        int height,
        int x,
        int y,
        ref double sum,
        ref int count)
    {
        if ((uint)x >= (uint)width || (uint)y >= (uint)height)
        {
            return;
        }

        sum += m[y, x];
        count++;
    }
}

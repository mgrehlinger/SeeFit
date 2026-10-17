namespace SeeFit.Fits;

public enum ScnrColor
{
    Green,
    Red,
    Blue
}

public enum ScnrMethod
{
    AverageNeutral,
    MaximumNeutral,
    MinimumNeutral,
    MaximumMask,
    AdditiveMask
}

public sealed class ScnrOptions
{
    public bool Enabled { get; init; }
    public ScnrColor Color { get; init; } = ScnrColor.Green;
    public ScnrMethod Method { get; init; } = ScnrMethod.AverageNeutral;
    public double Amount { get; init; } = 1.0;
    public bool PreserveLightness { get; init; } = true;
}

/// <summary>
/// Subtractive Chromatic Noise Reduction (PixInsight-compatible formulas).
/// </summary>
public static class Scnr
{
    public static void Apply(
        double[,] red,
        double[,] green,
        double[,] blue,
        int width,
        int height,
        ScnrOptions options)
    {
        if (!options.Enabled || width <= 0 || height <= 0)
        {
            return;
        }

        var amount = Math.Clamp(options.Amount, 0, 1);
        if (amount <= 0)
        {
            return;
        }

        // Mask methods assume normalized [0, 1] samples.
        var scale = options.Method is ScnrMethod.MaximumMask or ScnrMethod.AdditiveMask
            ? EstimateScale(red, green, blue, width, height)
            : 1.0;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var r = red[y, x];
                var g = green[y, x];
                var b = blue[y, x];
                var lightness = (r + g + b) / 3.0;

                GetOrderedChannels(options.Color, r, g, b, out var target, out var c1, out var c2);

                var rn1 = c1 / scale;
                var rn2 = c2 / scale;
                var tn = target / scale;

                var correctedNorm = Protect(tn, rn1, rn2, options.Method, amount);
                var corrected = correctedNorm * scale;

                // Neutral methods use Min(); blend by amount for partial strength.
                if (options.Method is ScnrMethod.AverageNeutral
                    or ScnrMethod.MaximumNeutral
                    or ScnrMethod.MinimumNeutral)
                {
                    corrected = target + (corrected - target) * amount;
                }

                SetOrderedChannels(options.Color, corrected, c1, c2, out r, out g, out b);

                if (options.PreserveLightness)
                {
                    var newLightness = (r + g + b) / 3.0;
                    if (newLightness > 1e-12)
                    {
                        var factor = lightness / newLightness;
                        r *= factor;
                        g *= factor;
                        b *= factor;
                    }
                }

                red[y, x] = r;
                green[y, x] = g;
                blue[y, x] = b;
            }
        }
    }

    private static double Protect(
        double target,
        double c1,
        double c2,
        ScnrMethod method,
        double amount)
    {
        return method switch
        {
            ScnrMethod.AverageNeutral => Math.Min(target, 0.5 * (c1 + c2)),
            ScnrMethod.MaximumNeutral => Math.Min(target, Math.Max(c1, c2)),
            ScnrMethod.MinimumNeutral => Math.Min(target, Math.Min(c1, c2)),
            ScnrMethod.MaximumMask => ApplyMask(target, Math.Max(c1, c2), amount),
            ScnrMethod.AdditiveMask => ApplyMask(target, Math.Min(1.0, c1 + c2), amount),
            _ => target
        };
    }

    private static double ApplyMask(double target, double mask, double amount)
    {
        // G' = G*(1-a)*(1-m) + m*G
        mask = Math.Clamp(mask, 0, 1);
        return target * (1.0 - amount) * (1.0 - mask) + mask * target;
    }

    private static void GetOrderedChannels(
        ScnrColor color,
        double r,
        double g,
        double b,
        out double target,
        out double c1,
        out double c2)
    {
        switch (color)
        {
            case ScnrColor.Red:
                target = r;
                c1 = g;
                c2 = b;
                break;
            case ScnrColor.Blue:
                target = b;
                c1 = r;
                c2 = g;
                break;
            default:
                target = g;
                c1 = r;
                c2 = b;
                break;
        }
    }

    private static void SetOrderedChannels(
        ScnrColor color,
        double target,
        double c1,
        double c2,
        out double r,
        out double g,
        out double b)
    {
        switch (color)
        {
            case ScnrColor.Red:
                r = target;
                g = c1;
                b = c2;
                break;
            case ScnrColor.Blue:
                r = c1;
                g = c2;
                b = target;
                break;
            default:
                r = c1;
                g = target;
                b = c2;
                break;
        }
    }

    private static double EstimateScale(
        double[,] red,
        double[,] green,
        double[,] blue,
        int width,
        int height)
    {
        double max = 0;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                max = Math.Max(max, Math.Abs(red[y, x]));
                max = Math.Max(max, Math.Abs(green[y, x]));
                max = Math.Max(max, Math.Abs(blue[y, x]));
            }
        }

        return max > 1e-12 ? max : 1.0;
    }
}

namespace SeeFit.Fits;

public enum BayerPattern
{
    Rggb,
    Bggr,
    Grbg,
    Gbrg
}

public static class BayerPatternParser
{
    public static BayerPattern? TryParse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim().Trim('\'').ToUpperInvariant();
        return normalized switch
        {
            "RGGB" => BayerPattern.Rggb,
            "BGGR" => BayerPattern.Bggr,
            "GRBG" => BayerPattern.Grbg,
            "GBRG" => BayerPattern.Gbrg,
            _ => null
        };
    }

    public static string ToDisplayName(BayerPattern pattern) => pattern switch
    {
        BayerPattern.Rggb => "RGGB",
        BayerPattern.Bggr => "BGGR",
        BayerPattern.Grbg => "GRBG",
        BayerPattern.Gbrg => "GBRG",
        _ => pattern.ToString()
    };

    /// <summary>
    /// Returns the captured channel at (x, y): 0 = R, 1 = G, 2 = B.
    /// </summary>
    public static int ChannelAt(BayerPattern pattern, int x, int y, int xOffset, int yOffset)
    {
        var col = (x + xOffset) & 1;
        var row = (y + yOffset) & 1;
        return pattern switch
        {
            BayerPattern.Rggb => (row, col) switch
            {
                (0, 0) => 0,
                (0, 1) => 1,
                (1, 0) => 1,
                _ => 2
            },
            BayerPattern.Bggr => (row, col) switch
            {
                (0, 0) => 2,
                (0, 1) => 1,
                (1, 0) => 1,
                _ => 0
            },
            BayerPattern.Grbg => (row, col) switch
            {
                (0, 0) => 1,
                (0, 1) => 0,
                (1, 0) => 2,
                _ => 1
            },
            BayerPattern.Gbrg => (row, col) switch
            {
                (0, 0) => 1,
                (0, 1) => 2,
                (1, 0) => 0,
                _ => 1
            },
            _ => 1
        };
    }
}

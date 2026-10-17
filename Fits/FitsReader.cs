using System.Globalization;
using System.IO;
using System.Text;

namespace SeeFit.Fits;

public static class FitsReader
{
    private const int BlockSize = 2880;
    private const int CardLength = 80;

    public static FitsImage Load(string path)
    {
        using var stream = File.OpenRead(path);
        return Load(stream);
    }

    public static FitsImage Load(Stream stream)
    {
        var keywords = ReadHeader(stream, out var bitPix, out var nAxis, out var axes);
        if (nAxis < 2)
        {
            throw new InvalidDataException("This FITS file does not contain a 2D image (NAXIS < 2).");
        }

        var width = axes[0];
        var height = axes[1];
        if (width <= 0 || height <= 0)
        {
            throw new InvalidDataException("Invalid image dimensions in FITS header.");
        }

        // For 3D cubes, display the first plane.
        var planeCount = nAxis >= 3 ? Math.Max(1, axes[2]) : 1;
        _ = planeCount;

        var bscale = GetDouble(keywords, "BSCALE", 1.0);
        var bzero = GetDouble(keywords, "BZERO", 0.0);
        var pixels = ReadPixelData(stream, width, height, bitPix, bscale, bzero);

        return new FitsImage
        {
            Keywords = keywords,
            Width = width,
            Height = height,
            BitPix = bitPix,
            PixelData = pixels,
            Preview = FitsImage.CreatePreview(pixels, width, height)
        };
    }

    private static List<FitsKeyword> ReadHeader(
        Stream stream,
        out int bitPix,
        out int nAxis,
        out int[] axes)
    {
        bitPix = 0;
        nAxis = 0;
        axes = [];

        var keywords = new List<FitsKeyword>();
        var ended = false;
        var buffer = new byte[BlockSize];

        while (!ended)
        {
            var read = stream.Read(buffer, 0, BlockSize);
            if (read < BlockSize)
            {
                throw new InvalidDataException("Unexpected end of file while reading FITS header.");
            }

            for (var offset = 0; offset < BlockSize; offset += CardLength)
            {
                var card = Encoding.ASCII.GetString(buffer, offset, CardLength);
                var keyword = ParseCard(card);
                keywords.Add(keyword);

                switch (keyword.Keyword)
                {
                    case "BITPIX":
                        bitPix = ParseInt(keyword.Value);
                        break;
                    case "NAXIS":
                        nAxis = ParseInt(keyword.Value);
                        axes = new int[Math.Max(nAxis, 0)];
                        break;
                    case "END":
                        ended = true;
                        break;
                    default:
                        if (keyword.Keyword.StartsWith("NAXIS", StringComparison.Ordinal)
                            && keyword.Keyword.Length > 5
                            && int.TryParse(keyword.Keyword.AsSpan(5), out var axisIndex)
                            && axisIndex >= 1
                            && axisIndex <= axes.Length)
                        {
                            axes[axisIndex - 1] = ParseInt(keyword.Value);
                        }

                        break;
                }

                if (ended)
                {
                    break;
                }
            }
        }

        if (bitPix == 0)
        {
            throw new InvalidDataException("Missing BITPIX keyword in FITS header.");
        }

        return keywords;
    }

    private static FitsKeyword ParseCard(string card)
    {
        var keyword = card[..8].Trim();
        if (keyword is "HISTORY" or "COMMENT" or "END" || keyword.Length == 0)
        {
            return new FitsKeyword
            {
                Keyword = string.IsNullOrEmpty(keyword) ? "(blank)" : keyword,
                Value = string.Empty,
                Comment = keyword is "HISTORY" or "COMMENT" ? card[8..].Trim() : string.Empty
            };
        }

        if (card.Length < 10 || card[8] != '=' )
        {
            return new FitsKeyword
            {
                Keyword = keyword,
                Value = string.Empty,
                Comment = card[8..].Trim()
            };
        }

        var valueAndComment = card[10..];
        string value;
        string comment;

        if (valueAndComment.Length > 0 && valueAndComment[0] == '\'')
        {
            var endQuote = FindClosingQuote(valueAndComment);
            value = valueAndComment[1..endQuote].Replace("''", "'", StringComparison.Ordinal);
            var remainder = valueAndComment[(endQuote + 1)..];
            var slash = remainder.IndexOf('/');
            comment = slash >= 0 ? remainder[(slash + 1)..].Trim() : remainder.Trim();
        }
        else
        {
            var slash = valueAndComment.IndexOf('/');
            if (slash >= 0)
            {
                value = valueAndComment[..slash].Trim();
                comment = valueAndComment[(slash + 1)..].Trim();
            }
            else
            {
                value = valueAndComment.Trim();
                comment = string.Empty;
            }
        }

        return new FitsKeyword
        {
            Keyword = keyword,
            Value = value,
            Comment = comment
        };
    }

    private static int FindClosingQuote(string valueAndComment)
    {
        for (var i = 1; i < valueAndComment.Length; i++)
        {
            if (valueAndComment[i] != '\'')
            {
                continue;
            }

            if (i + 1 < valueAndComment.Length && valueAndComment[i + 1] == '\'')
            {
                i++;
                continue;
            }

            return i;
        }

        return valueAndComment.Length - 1;
    }

    private static double[,] ReadPixelData(
        Stream stream,
        int width,
        int height,
        int bitPix,
        double bscale,
        double bzero)
    {
        var bytesPerPixel = Math.Abs(bitPix) / 8;
        var pixelCount = width * height;
        var byteCount = checked(pixelCount * bytesPerPixel);
        var raw = new byte[byteCount];
        var totalRead = 0;
        while (totalRead < byteCount)
        {
            var read = stream.Read(raw, totalRead, byteCount - totalRead);
            if (read == 0)
            {
                throw new InvalidDataException("Unexpected end of file while reading FITS image data.");
            }

            totalRead += read;
        }

        var pixels = new double[height, width];
        var offset = 0;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                double value = bitPix switch
                {
                    8 => raw[offset],
                    16 => ReadInt16BigEndian(raw, offset),
                    32 => ReadInt32BigEndian(raw, offset),
                    64 => ReadInt64BigEndian(raw, offset),
                    -32 => ReadSingleBigEndian(raw, offset),
                    -64 => ReadDoubleBigEndian(raw, offset),
                    _ => throw new NotSupportedException($"Unsupported BITPIX value: {bitPix}")
                };

                pixels[y, x] = value * bscale + bzero;
                offset += bytesPerPixel;
            }
        }

        return pixels;
    }

    private static short ReadInt16BigEndian(byte[] data, int offset) =>
        (short)((data[offset] << 8) | data[offset + 1]);

    private static int ReadInt32BigEndian(byte[] data, int offset) =>
        (data[offset] << 24)
        | (data[offset + 1] << 16)
        | (data[offset + 2] << 8)
        | data[offset + 3];

    private static long ReadInt64BigEndian(byte[] data, int offset) =>
        ((long)data[offset] << 56)
        | ((long)data[offset + 1] << 48)
        | ((long)data[offset + 2] << 40)
        | ((long)data[offset + 3] << 32)
        | ((long)data[offset + 4] << 24)
        | ((long)data[offset + 5] << 16)
        | ((long)data[offset + 6] << 8)
        | data[offset + 7];

    private static float ReadSingleBigEndian(byte[] data, int offset)
    {
        Span<byte> local = stackalloc byte[4];
        local[0] = data[offset + 3];
        local[1] = data[offset + 2];
        local[2] = data[offset + 1];
        local[3] = data[offset];
        return BitConverter.ToSingle(local);
    }

    private static double ReadDoubleBigEndian(byte[] data, int offset)
    {
        Span<byte> local = stackalloc byte[8];
        for (var i = 0; i < 8; i++)
        {
            local[i] = data[offset + 7 - i];
        }

        return BitConverter.ToDouble(local);
    }

    private static int ParseInt(string value) =>
        int.Parse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture);

    private static double GetDouble(IEnumerable<FitsKeyword> keywords, string name, double fallback)
    {
        var match = keywords.FirstOrDefault(k =>
            string.Equals(k.Keyword, name, StringComparison.OrdinalIgnoreCase));
        if (match is null || string.IsNullOrWhiteSpace(match.Value))
        {
            return fallback;
        }

        return double.TryParse(match.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;
    }
}

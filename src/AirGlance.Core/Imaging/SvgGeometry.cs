using System.Globalization;
using System.Xml;

namespace AirGlance.Core.Imaging;

/// <summary>
/// Reads the intrinsic size from the root SVG element without rasterizing it.
/// The SVG specification uses 300 x 150 when both width and height are omitted.
/// </summary>
public static class SvgGeometry
{
    private const int DefaultWidth = 300;
    private const int DefaultHeight = 150;
    private const int MaximumDimension = 32767;
    private const long MaximumDocumentCharacters = 8 * 1024 * 1024;

    public static (int Width, int Height) Read(string path)
    {
        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 64 * 1024,
                FileOptions.SequentialScan);

            // Read the root element with XmlReader: a full XDocument was built here only
            // to read three attributes, and the document size is now bounded.
            using var reader = XmlReader.Create(
                stream,
                new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null,
                    IgnoreComments = true,
                    IgnoreProcessingInstructions = true,
                    MaxCharactersInDocument = MaximumDocumentCharacters
                });

            if (reader.MoveToContent() != XmlNodeType.Element ||
                !string.Equals(reader.LocalName, "svg", StringComparison.OrdinalIgnoreCase))
            {
                return (DefaultWidth, DefaultHeight);
            }

            var viewBox = ParseViewBox(reader.GetAttribute("viewBox"));
            var width = ParseLength(reader.GetAttribute("width"));
            var height = ParseLength(reader.GetAttribute("height"));

            if (width is null && height is null && viewBox is { } box)
            {
                width = box.Width;
                height = box.Height;
            }
            else if (width is null && viewBox is { } fromHeight)
            {
                width = height is > 0 ? height.Value * fromHeight.Width / fromHeight.Height : fromHeight.Width;
            }
            else if (height is null && viewBox is { } fromWidth)
            {
                height = width is > 0 ? width.Value * fromWidth.Height / fromWidth.Width : fromWidth.Height;
            }

            return (
                ClampDimension(width ?? DefaultWidth),
                ClampDimension(height ?? DefaultHeight));
        }
        catch
        {
            return (DefaultWidth, DefaultHeight);
        }
    }

    private static int ClampDimension(double value)
    {
        if (!double.IsFinite(value) || value <= 0)
        {
            return 1;
        }

        return Math.Clamp((int)Math.Round(value), 1, MaximumDimension);
    }

    private static double? ParseLength(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = value.Trim();
        if (text.EndsWith('%'))
        {
            return null;
        }

        var end = 0;
        while (end < text.Length &&
               (char.IsDigit(text[end]) || text[end] is '.' or '-' or '+' or 'e' or 'E'))
        {
            end++;
        }

        return end > 0 &&
               double.TryParse(text[..end], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) &&
               parsed > 0
            ? parsed
            : null;
    }

    private static (double Width, double Height)? ParseViewBox(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var parts = value
            .Split([' ', ',', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4 ||
            !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var width) ||
            !double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var height) ||
            width <= 0 ||
            height <= 0)
        {
            return null;
        }

        return (width, height);
    }
}

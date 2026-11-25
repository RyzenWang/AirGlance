namespace AirGlance.Core.Imaging;

/// <summary>
/// Computes the analysis drawer's numbers from decoded display pixels. The pixels are
/// BGRA8888, premultiplied and already converted to sRGB. The analyzer does not
/// allocate a second pixel buffer and never returns one.
/// </summary>
public static class ImageAnalyzer
{
    public const int UniqueColorLimit = 65_536;
    private const int PaletteSampleLimit = 65_536;
    private const int HueBins = 36;
    private const int SaturationBins = 64;
    private const int DetailEdge = 1024;

    /// <summary>One bit per possible 8-bit-per-channel colour, 2^24 bits in total.</summary>
    private const int UniqueColorMapWords = (1 << 24) >> 6;

    public static ImageAnalysisResult Analyze(
        DecodedBitmap image,
        ImageAnalysisContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(context);

        var width = image.Width;
        var height = image.Height;
        var total = width * height;
        var pixels = image.Pixels;

        var luma = new int[256];
        var red = new int[256];
        var green = new int[256];
        var blue = new int[256];
        var hue = new int[HueBins];
        var saturation = new int[SaturationBins];
        var alpha = new int[256];
        var zones = new long[11];

        // Counting distinct colours by hashing every pixel was the most expensive thing
        // in an otherwise five-operation loop: 24 M hash inserts on a 24 MP image. A
        // 2 MB presence map answers the same question with one AND and one OR. Small
        // images keep the set, where the 2 MB allocation would be the larger cost.
        var uniqueBits = total >= 65_536 ? new ulong[UniqueColorMapWords] : null;
        var uniqueSet = uniqueBits is null ? new HashSet<int>() : null;
        var uniqueCount = 0;

        long pixelCount = 0;
        long transparent = 0;
        long semiTransparent = 0;
        long shadowClip = 0;
        long highlightClip = 0;
        long hardBlack = 0;
        long hardWhite = 0;
        double sumRed = 0;
        double sumGreen = 0;
        double sumBlue = 0;
        double sumSaturation = 0;
        var uniqueCapped = false;

        for (var index = 0; index < total; index++)
        {
            if ((index & 0x3FFFF) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var offset = index * 4;
            var b = pixels[offset];
            var g = pixels[offset + 1];
            var r = pixels[offset + 2];
            var a = pixels[offset + 3];

            alpha[a]++;
            if (a == 0)
            {
                transparent++;
                continue;
            }

            if (a != 255)
            {
                semiTransparent++;
                b = Unpremultiply(b, a);
                g = Unpremultiply(g, a);
                r = Unpremultiply(r, a);
            }

            pixelCount++;
            var luminance = Luma(r, g, b);
            luma[luminance]++;
            red[r]++;
            green[g]++;
            blue[b]++;

            if (luminance <= 2)
            {
                shadowClip++;
            }

            if (luminance >= 253)
            {
                highlightClip++;
            }

            if (luminance == 0)
            {
                hardBlack++;
            }
            else if (luminance == 255)
            {
                hardWhite++;
            }

            zones[Math.Min(10, luminance * 11 / 256)]++;

            var max = Math.Max(r, Math.Max(g, b));
            var min = Math.Min(r, Math.Min(g, b));
            var maxMinusMin = max - min;
            var denominator = 255.0 - Math.Abs(max + min - 255.0);
            var hslSaturation = denominator <= 0 ? 0 : maxMinusMin / denominator;
            sumSaturation += hslSaturation;
            saturation[Math.Clamp((int)(hslSaturation * SaturationBins), 0, SaturationBins - 1)]++;

            if (maxMinusMin > 0 && hslSaturation >= 0.10 && max > 12)
            {
                var h = max == r
                    ? (g - b) * 60.0 / maxMinusMin
                    : max == g
                        ? (b - r) * 60.0 / maxMinusMin + 120
                        : (r - g) * 60.0 / maxMinusMin + 240;
                if (h < 0)
                {
                    h += 360;
                }

                var hueIndex = Math.Clamp((int)(h / 10), 0, HueBins - 1);
                hue[hueIndex]++;
            }

            sumRed += r;
            sumGreen += g;
            sumBlue += b;

            if (!uniqueCapped)
            {
                var packed = (r << 16) | (g << 8) | b;
                if (uniqueBits is not null)
                {
                    ref var word = ref uniqueBits[packed >> 6];
                    var mask = 1UL << (packed & 63);
                    if ((word & mask) == 0)
                    {
                        word |= mask;
                        uniqueCount++;
                    }
                }
                else if (uniqueSet!.Add(packed))
                {
                    uniqueCount++;
                }

                if (uniqueCount >= UniqueColorLimit)
                {
                    uniqueCapped = true;
                }
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        var (mean, median, stdDev, entropy) = ComputeLumaStats(luma, pixelCount);
        var (sharpness, noise) = ComputeDetail(pixels, width, height, cancellationToken);
        var palette = BuildPalette(pixels, total, cancellationToken);

        var average = pixelCount == 0
            ? default
            : new RgbColor(
                (byte)Math.Round(sumRed / pixelCount),
                (byte)Math.Round(sumGreen / pixelCount),
                (byte)Math.Round(sumBlue / pixelCount));

        return new ImageAnalysisResult
        {
            Width = width,
            Height = height,
            PixelCount = pixelCount,
            Luma = luma,
            Red = red,
            Green = green,
            Blue = blue,
            Hue = hue,
            Saturation = saturation,
            Alpha = alpha,
            Mean = mean,
            Median = median,
            StdDev = stdDev,
            ShadowClipCount = shadowClip,
            HighlightClipCount = highlightClip,
            HardBlackCount = hardBlack,
            HardWhiteCount = hardWhite,
            ZoneCounts = zones,
            Entropy = entropy,
            UniqueColorCount = uniqueCapped ? UniqueColorLimit : uniqueCount,
            UniqueColorCapped = uniqueCapped,
            HasTransparency = transparent > 0 || semiTransparent > 0,
            TransparentCount = transparent,
            SemiTransparentCount = semiTransparent,
            AverageColor = average,
            AverageSaturation = pixelCount == 0 ? 0 : sumSaturation / pixelCount,
            Warmth = pixelCount == 0 ? 0 : (sumRed - sumBlue) / pixelCount,
            Palette = palette,
            Sharpness = sharpness,
            NoiseSigma = noise,
            FileBytes = context.FileBytes,
            FrameCount = context.FrameCount,
            DurationSeconds = context.DurationSeconds,
            LoopCount = context.LoopCount,
            Jpeg = context.Jpeg
        };
    }

    private static (double Mean, int Median, double StdDev, double Entropy) ComputeLumaStats(
        int[] histogram,
        long pixelCount)
    {
        if (pixelCount == 0)
        {
            return (0, 0, 0, 0);
        }

        double sum = 0;
        for (var i = 0; i < histogram.Length; i++)
        {
            sum += (long)i * histogram[i];
        }

        var mean = sum / pixelCount;
        double variance = 0;
        double entropy = 0;
        for (var i = 0; i < histogram.Length; i++)
        {
            var count = histogram[i];
            if (count == 0)
            {
                continue;
            }

            variance += count * (i - mean) * (i - mean);
            var p = count / (double)pixelCount;
            entropy -= p * Math.Log2(p);
        }

        var target = (pixelCount + 1) / 2;
        long cumulative = 0;
        var median = 0;
        for (var i = 0; i < histogram.Length; i++)
        {
            cumulative += histogram[i];
            if (cumulative >= target)
            {
                median = i;
                break;
            }
        }

        return (mean, median, Math.Sqrt(variance / pixelCount), entropy);
    }

    private static (double Sharpness, double NoiseSigma) ComputeDetail(
        byte[] pixels,
        int width,
        int height,
        CancellationToken cancellationToken)
    {
        if (width < 3 || height < 3)
        {
            return (0, 0);
        }

        var factor = Math.Max(1, (int)Math.Ceiling(Math.Max(width, height) / (double)DetailEdge));
        var smallWidth = Math.Max(1, width / factor);
        var smallHeight = Math.Max(1, height / factor);
        if (smallWidth < 3 || smallHeight < 3)
        {
            return (0, 0);
        }

        var small = new byte[smallWidth * smallHeight];
        for (var y = 0; y < smallHeight; y++)
        {
            if ((y & 63) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            for (var x = 0; x < smallWidth; x++)
            {
                long sum = 0;
                var count = 0;
                var startY = y * factor;
                var startX = x * factor;
                var endY = Math.Min(height, startY + factor);
                var endX = Math.Min(width, startX + factor);
                for (var py = startY; py < endY; py++)
                {
                    var row = py * width;
                    for (var px = startX; px < endX; px++)
                    {
                        var offset = (row + px) * 4;
                        var a = pixels[offset + 3];
                        if (a == 0)
                        {
                            continue;
                        }

                        var b = pixels[offset];
                        var g = pixels[offset + 1];
                        var r = pixels[offset + 2];
                        if (a != 255)
                        {
                            b = Unpremultiply(b, a);
                            g = Unpremultiply(g, a);
                            r = Unpremultiply(r, a);
                        }

                        sum += Luma(r, g, b);
                        count++;
                    }
                }

                small[y * smallWidth + x] = count == 0 ? (byte)0 : (byte)(sum / count);
            }
        }

        double lumaSum = 0;
        double lumaSumSquares = 0;
        for (var i = 0; i < small.Length; i++)
        {
            lumaSum += small[i];
            lumaSumSquares += small[i] * small[i];
        }

        var mean = lumaSum / small.Length;
        var variance = Math.Max(0, lumaSumSquares / small.Length - mean * mean);
        var contrast = Math.Sqrt(variance);

        double lapSum = 0;
        long lapCount = 0;
        for (var y = 1; y < smallHeight - 1; y++)
        {
            for (var x = 1; x < smallWidth - 1; x++)
            {
                var center = small[y * smallWidth + x];
                var up = small[(y - 1) * smallWidth + x];
                var down = small[(y + 1) * smallWidth + x];
                var left = small[y * smallWidth + x - 1];
                var right = small[y * smallWidth + x + 1];
                lapSum += Math.Abs(4 * center - up - down - left - right);
                lapCount++;
            }
        }

        if (lapCount == 0)
        {
            return (0, 0);
        }

        var meanLaplacian = lapSum / lapCount;
        var sharpness = Math.Clamp(meanLaplacian / (contrast + 8) / 0.8 * 100, 0, 100);
        var noise = Math.Sqrt(Math.PI / 2) / 6 * meanLaplacian;
        return (sharpness, noise);
    }

    private static IReadOnlyList<PaletteColor> BuildPalette(
        byte[] pixels,
        int total,
        CancellationToken cancellationToken)
    {
        var stride = Math.Max(1, total / PaletteSampleLimit);
        var sampleL = new List<double>(Math.Min(total, PaletteSampleLimit));
        var sampleA = new List<double>(sampleL.Capacity);
        var sampleB = new List<double>(sampleL.Capacity);

        for (var index = 0; index < total; index += stride)
        {
            var offset = index * 4;
            var a = pixels[offset + 3];
            if (a == 0)
            {
                continue;
            }

            var b = pixels[offset];
            var g = pixels[offset + 1];
            var r = pixels[offset + 2];
            if (a != 255)
            {
                b = Unpremultiply(b, a);
                g = Unpremultiply(g, a);
                r = Unpremultiply(r, a);
            }

            var (l, oa, ob) = ToOkLab(r, g, b);
            sampleL.Add(l);
            sampleA.Add(oa);
            sampleB.Add(ob);
        }

        if (sampleL.Count == 0)
        {
            return [];
        }

        cancellationToken.ThrowIfCancellationRequested();
        var n = sampleL.Count;
        var k = Math.Min(6, n);
        var centroidsL = new double[k];
        var centroidsA = new double[k];
        var centroidsB = new double[k];
        for (var i = 0; i < k; i++)
        {
            var index = (int)((long)i * n / k);
            centroidsL[i] = sampleL[index];
            centroidsA[i] = sampleA[index];
            centroidsB[i] = sampleB[index];
        }

        var assignments = new int[n];
        for (var iteration = 0; iteration < 16; iteration++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sumsL = new double[k];
            var sumsA = new double[k];
            var sumsB = new double[k];
            var counts = new int[k];

            for (var i = 0; i < n; i++)
            {
                var best = 0;
                var bestDistance = double.MaxValue;
                for (var c = 0; c < k; c++)
                {
                    var dl = sampleL[i] - centroidsL[c];
                    var da = sampleA[i] - centroidsA[c];
                    var db = sampleB[i] - centroidsB[c];
                    var distance = dl * dl + da * da + db * db;
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = c;
                    }
                }

                assignments[i] = best;
                sumsL[best] += sampleL[i];
                sumsA[best] += sampleA[i];
                sumsB[best] += sampleB[i];
                counts[best]++;
            }

            for (var c = 0; c < k; c++)
            {
                if (counts[c] == 0)
                {
                    continue;
                }

                centroidsL[c] = sumsL[c] / counts[c];
                centroidsA[c] = sumsA[c] / counts[c];
                centroidsB[c] = sumsB[c] / counts[c];
            }
        }

        var clusterCounts = new int[k];
        for (var i = 0; i < n; i++)
        {
            clusterCounts[assignments[i]]++;
        }

        var mergedL = new List<double>(k);
        var mergedA = new List<double>(k);
        var mergedB = new List<double>(k);
        var mergedCounts = new List<int>(k);
        for (var c = 0; c < k; c++)
        {
            if (clusterCounts[c] == 0)
            {
                continue;
            }

            var found = false;
            for (var m = 0; m < mergedL.Count; m++)
            {
                var dl = centroidsL[c] - mergedL[m];
                var da = centroidsA[c] - mergedA[m];
                var db = centroidsB[c] - mergedB[m];
                if (dl * dl + da * da + db * db >= 0.03 * 0.03)
                {
                    continue;
                }

                var totalCount = mergedCounts[m] + clusterCounts[c];
                mergedL[m] = (mergedL[m] * mergedCounts[m] + centroidsL[c] * clusterCounts[c]) / totalCount;
                mergedA[m] = (mergedA[m] * mergedCounts[m] + centroidsA[c] * clusterCounts[c]) / totalCount;
                mergedB[m] = (mergedB[m] * mergedCounts[m] + centroidsB[c] * clusterCounts[c]) / totalCount;
                mergedCounts[m] = totalCount;
                found = true;
                break;
            }

            if (!found)
            {
                mergedL.Add(centroidsL[c]);
                mergedA.Add(centroidsA[c]);
                mergedB.Add(centroidsB[c]);
                mergedCounts.Add(clusterCounts[c]);
            }
        }

        var palette = new List<PaletteColor>(mergedCounts.Count);
        for (var i = 0; i < mergedCounts.Count; i++)
        {
            var color = FromOkLab(mergedL[i], mergedA[i], mergedB[i]);
            palette.Add(new PaletteColor(color, mergedCounts[i] / (double)n));
        }

        palette.Sort((left, right) => right.Share.CompareTo(left.Share));
        return palette;
    }

    private static (double L, double A, double B) ToOkLab(byte red, byte green, byte blue)
    {
        var r = ToLinear(red / 255.0);
        var g = ToLinear(green / 255.0);
        var b = ToLinear(blue / 255.0);

        var l = 0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b;
        var m = 0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b;
        var s = 0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b;
        var l_ = Math.Cbrt(l);
        var m_ = Math.Cbrt(m);
        var s_ = Math.Cbrt(s);

        return (
            0.2104542553 * l_ + 0.7936177850 * m_ - 0.0040720468 * s_,
            1.9779984951 * l_ - 2.4285922050 * m_ + 0.4505937099 * s_,
            0.0259040371 * l_ + 0.7827717662 * m_ - 0.8086757660 * s_);
    }

    private static RgbColor FromOkLab(double lightness, double a, double b)
    {
        var l_ = lightness + 0.3963377774 * a + 0.2158037573 * b;
        var m_ = lightness - 0.1055613458 * a - 0.0638541728 * b;
        var s_ = lightness - 0.0894841775 * a - 1.2914855480 * b;
        var l = l_ * l_ * l_;
        var m = m_ * m_ * m_;
        var s = s_ * s_ * s_;

        var r = 4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s;
        var g = -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s;
        var bl = -0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s;
        return new RgbColor(ToSrgb(r), ToSrgb(g), ToSrgb(bl));
    }

    private static byte ToSrgb(double value)
    {
        var encoded = value <= 0.0031308
            ? 12.92 * value
            : 1.055 * Math.Pow(value, 1.0 / 2.4) - 0.055;
        return (byte)Math.Clamp(Math.Round(encoded * 255), 0, 255);
    }

    private static double ToLinear(double value) =>
        value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);

    private static int Luma(byte red, byte green, byte blue) =>
        (54 * red + 183 * green + 19 * blue + 128) >> 8;

    private static byte Unpremultiply(byte value, byte alpha)
    {
        if (alpha == 0)
        {
            return 0;
        }

        if (alpha == 255)
        {
            return value;
        }

        var result = (value * 255 + alpha / 2) / alpha;
        return result > 255 ? (byte)255 : (byte)result;
    }
}

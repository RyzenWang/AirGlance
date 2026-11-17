using System.Diagnostics;
using System.Globalization;

namespace AirGlance.App.Services;

/// <summary>
/// Records the numbers <c>tools/bench.ps1</c> reports. Times are measured from the
/// operating system's process start time, so they include runtime startup, not just
/// the part after <c>Main</c>.
/// </summary>
public sealed class BenchmarkRecorder
{
    private readonly Action<int> _shutdown;
    private readonly DateTime _processStartedAt;
    private double? _windowShownMs;
    private double? _firstFrameMs;
    private double? _decodeMs;
    private double? _megapixels;
    private bool _finished;

    public BenchmarkRecorder(BenchmarkMode mode, Action<int> shutdown)
    {
        Mode = mode;
        _shutdown = shutdown;
        _processStartedAt = GetProcessStartTime();
    }

    public BenchmarkMode Mode { get; }

    public void MarkWindowShown()
    {
        if (_windowShownMs is null)
        {
            _windowShownMs = ElapsedMilliseconds();
        }
    }

    public void MarkFirstFrame(double decodeMilliseconds, double megapixels)
    {
        if (_firstFrameMs is not null)
        {
            return;
        }

        _firstFrameMs = ElapsedMilliseconds();
        _decodeMs = decodeMilliseconds;
        _megapixels = megapixels;

        if (Mode == BenchmarkMode.Startup)
        {
            Finish(0);
        }
    }

    public void MarkDecode(double decodeMilliseconds, double megapixels)
    {
        _decodeMs = decodeMilliseconds;
        _megapixels = megapixels;
        _firstFrameMs = 0;
        Finish(0);
    }

    public void MarkScan(double scanMilliseconds, double thumbnailMilliseconds, int fileCount)
    {
        if (_finished)
        {
            return;
        }

        _finished = true;
        var process = Process.GetCurrentProcess();
        var peakMegabytes = process.PeakWorkingSet64 / (1024.0 * 1024.0);

        Console.Out.WriteLine($"scan_ms={scanMilliseconds.ToString("0.0", CultureInfo.InvariantCulture)}");
        Console.Out.WriteLine($"thumbnail_ms={thumbnailMilliseconds.ToString("0.0", CultureInfo.InvariantCulture)}");
        Console.Out.WriteLine($"total_ms={(scanMilliseconds + thumbnailMilliseconds).ToString("0.0", CultureInfo.InvariantCulture)}");
        Console.Out.WriteLine($"files={fileCount}");
        Console.Out.WriteLine($"peak_ws_mb={peakMegabytes.ToString("0.0", CultureInfo.InvariantCulture)}");
        Console.Out.Flush();
        _shutdown(0);
    }

    public void Fail(string message)
    {
        if (_finished)
        {
            return;
        }

        _finished = true;
        Console.Out.WriteLine("error=" + message.ReplaceLineEndings(" "));
        Console.Out.Flush();
        _shutdown(1);
    }

    private void Finish(int exitCode)
    {
        if (_finished)
        {
            return;
        }

        _finished = true;

        var process = Process.GetCurrentProcess();
        var peakMegabytes = process.PeakWorkingSet64 / (1024.0 * 1024.0);

        if (Mode == BenchmarkMode.Startup || Mode == BenchmarkMode.Decode)
        {
            Console.Out.WriteLine($"first_window_ms={Format(_windowShownMs)}");
            Console.Out.WriteLine($"first_frame_ms={Format(_firstFrameMs)}");
            Console.Out.WriteLine($"decode_ms={Format(_decodeMs)}");
            Console.Out.WriteLine($"image_mp={Format(_megapixels, "0.0")}");
            Console.Out.WriteLine($"peak_ws_mb={peakMegabytes.ToString("0.0", CultureInfo.InvariantCulture)}");
            Console.Out.Flush();
        }

        _shutdown(exitCode);
    }

    private double ElapsedMilliseconds() =>
        (DateTime.Now - _processStartedAt).TotalMilliseconds;

    private static string Format(double? value, string format = "0.0") =>
        value is null ? "0" : value.Value.ToString(format, CultureInfo.InvariantCulture);

    private static DateTime GetProcessStartTime()
    {
        try
        {
            return Process.GetCurrentProcess().StartTime;
        }
        catch
        {
            return DateTime.Now;
        }
    }
}

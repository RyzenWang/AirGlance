using System.Diagnostics;
using AirGlance.App.Imaging;
using AirGlance.App.Services;
using AirGlance.App.Views;
using AirGlance.Core.Files;
using AirGlance.Core.Imaging;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace AirGlance.App;

public partial class App : Application
{
    public override void Initialize()
    {
        StartupTrace.Mark("app-init");
        AvaloniaXamlLoader.Load(this);
        StartupTrace.Mark("xaml-loaded");
    }

    public override void OnFrameworkInitializationCompleted()
    {
        StartupTrace.Mark("framework-init");
        Dispatcher.UIThread.UnhandledException += (_, args) =>
            Console.Error.WriteLine("AirGlance unhandled: " + args.Exception);

        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            base.OnFrameworkInitializationCompleted();
            return;
        }

        if (Environment.GetEnvironmentVariable("AIRGLANCE_HEADLESS") == "1")
        {
            // The headless test host builds windows itself; do not open the real one.
            base.OnFrameworkInitializationCompleted();
            return;
        }

        var options = CommandLineOptions.Parse(desktop.Args);
        var services = new AppServices();
        TiledImageSource.CleanupStale(TimeSpan.FromDays(1));
        if (options.Benchmark != BenchmarkMode.None)
        {
            services.SaveSettingsOnDispose = false;
        }

        StartupTrace.Mark("services-ready");

        if (options.Benchmark is BenchmarkMode.Decode or BenchmarkMode.Scan)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var recorder = new BenchmarkRecorder(options.Benchmark, desktop.Shutdown);
            _ = Task.Run(() =>
            {
                if (options.Benchmark == BenchmarkMode.Scan)
                {
                    RunScanBenchmark(options, recorder);
                }
                else
                {
                    RunDecodeBenchmark(options, recorder);
                }
            });
            base.OnFrameworkInitializationCompleted();
            return;
        }

        BenchmarkRecorder? benchmark = options.Benchmark == BenchmarkMode.Startup
            ? new BenchmarkRecorder(BenchmarkMode.Startup, desktop.Shutdown)
            : null;

        Task<PreloadedImage?>? preload = null;
        if (options.TargetPath is not null && (options.ShowHelp || options.ShowVersion) == false)
        {
            var target = options.TargetPath;
            preload = Task.Run(() => StartupImageLoader.TryLoad(target));
        }

        var window = new MainWindow(services, options, benchmark, preload);
        StartupTrace.Mark("window-created");
        desktop.MainWindow = window;
        desktop.Exit += (_, _) => services.Dispose();

        base.OnFrameworkInitializationCompleted();
    }

    private static void RunScanBenchmark(CommandLineOptions options, BenchmarkRecorder recorder)
    {
        try
        {
            var path = options.TargetPath;
            if (path is null)
            {
                recorder.Fail("no folder path");
                return;
            }

            var folder = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
            if (folder is null || !Directory.Exists(folder))
            {
                recorder.Fail("no folder path");
                return;
            }

            var scan = Stopwatch.StartNew();
            var files = FolderScanner.EnumerateImages(folder);
            scan.Stop();

            var thumbnail = Stopwatch.StartNew();
            foreach (var file in files.Take(8))
            {
                _ = ImageDecoder.Decode(file, 160, 160);
            }

            thumbnail.Stop();
            recorder.MarkScan(scan.Elapsed.TotalMilliseconds, thumbnail.Elapsed.TotalMilliseconds, files.Count);
        }
        catch (Exception exception)
        {
            recorder.Fail(exception.Message);
        }
    }

    private static void RunDecodeBenchmark(CommandLineOptions options, BenchmarkRecorder recorder)
    {
        try
        {
            var path = options.TargetPath;
            if (path is null)
            {
                recorder.Fail("no image path");
                return;
            }

            if (Directory.Exists(path))
            {
                var files = FolderScanner.EnumerateImages(path);
                if (files.Count == 0)
                {
                    recorder.Fail("no image in folder");
                    return;
                }

                path = files[0];
            }

            var stopwatch = Stopwatch.StartNew();
            var bitmap = ImageDecoder.Decode(path, 1600, 1000);
            stopwatch.Stop();

            recorder.MarkDecode(
                stopwatch.Elapsed.TotalMilliseconds,
                bitmap.SourceWidth * (double)bitmap.SourceHeight / 1_000_000d);
        }
        catch (Exception exception)
        {
            recorder.Fail(exception.Message);
        }
    }
}



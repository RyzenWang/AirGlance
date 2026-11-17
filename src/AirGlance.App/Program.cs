using Avalonia;
using AirGlance.App;
using AirGlance.App.Services;

namespace AirGlance;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        StartupTrace.Mark("main");
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder
            .Configure<AirGlance.App.App>()
            .UsePlatformDetect()
            .WithInterFont();

        if (OperatingSystem.IsWindows() &&
            Environment.GetEnvironmentVariable("AIRGLANCE_GPU_RENDER") != "1")
        {
            // The software compositor starts faster and uses less memory than ANGLE
            // on Windows. The viewer scales one bitmap, which software rasterisation
            // handles well; set AIRGLANCE_GPU_RENDER=1 to try the GPU path.
            builder = builder.With(new Win32PlatformOptions
            {
                RenderingMode = [Win32RenderingMode.Software]
            });
        }

#if DEBUG
        builder = builder.LogToTrace();
#endif

        return builder;
    }
}

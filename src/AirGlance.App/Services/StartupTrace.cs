using System.Diagnostics;

namespace AirGlance.App.Services;

/// <summary>
/// Opt-in timing marks for startup investigations. Set AIRGLANCE_STARTUP_TRACE=1 to
/// print them to stderr; normal runs pay only a static read of the environment.
/// </summary>
internal static class StartupTrace
{
    private static readonly bool Enabled =
        Environment.GetEnvironmentVariable("AIRGLANCE_STARTUP_TRACE") == "1";

    private static readonly DateTime? Start = GetStart();

    public static void Mark(string stage)
    {
        if (!Enabled)
        {
            return;
        }

        var elapsed = Start is null ? 0 : (DateTime.Now - Start.Value).TotalMilliseconds;
        Console.Error.WriteLine($"{stage}={elapsed:0.0}");
    }

    private static DateTime? GetStart()
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

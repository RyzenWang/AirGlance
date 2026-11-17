using AirGlance.Core.Files;

namespace AirGlance.App.Services;

public enum BenchmarkMode
{
    None,
    Startup,
    Decode,
    Scan
}

/// <summary>
/// Parsed command line. The first displayable file or folder wins; other arguments
/// are kept only so a help message can mention them.
/// </summary>
public sealed class CommandLineOptions
{
    private CommandLineOptions()
    {
    }

    public bool ShowHelp { get; private init; }

    public bool ShowVersion { get; private init; }

    public BenchmarkMode Benchmark { get; private init; }

    public string? TargetPath { get; private init; }

    public IReadOnlyList<string> IgnoredPaths { get; private init; } = [];

    public IReadOnlyList<string> UnknownOptions { get; private init; } = [];

    public static CommandLineOptions Parse(IReadOnlyList<string>? args)
    {
        var paths = new List<string>();
        var unknown = new List<string>();
        var showHelp = false;
        var showVersion = false;
        var benchmark = BenchmarkMode.None;
        var pathsOnly = false;

        if (args is not null)
        {
            foreach (var argument in args)
            {
                if (string.IsNullOrWhiteSpace(argument))
                {
                    continue;
                }

                if (pathsOnly)
                {
                    paths.Add(argument);
                    continue;
                }

                switch (argument)
                {
                    case "--":
                        pathsOnly = true;
                        continue;
                    case "-h" or "--help" or "-?":
                        showHelp = true;
                        continue;
                    case "--version" or "-v":
                        showVersion = true;
                        continue;
                    case "--bench=startup":
                        benchmark = BenchmarkMode.Startup;
                        continue;
                    case "--bench=decode":
                        benchmark = BenchmarkMode.Decode;
                        continue;
                    case "--bench=scan":
                        benchmark = BenchmarkMode.Scan;
                        continue;
                }

                if (argument.StartsWith("--bench=", StringComparison.Ordinal))
                {
                    unknown.Add(argument);
                    continue;
                }

                if (argument.Length > 1 && argument[0] == '-')
                {
                    unknown.Add(argument);
                    continue;
                }

                paths.Add(argument);
            }
        }

        string? target = null;
        var ignored = new List<string>();
        var firstExisting = paths.FirstOrDefault(
            path => File.Exists(path) || Directory.Exists(path));

        foreach (var path in paths)
        {
            if (target is null && Directory.Exists(path))
            {
                target = path;
                continue;
            }

            if (target is null && File.Exists(path) && SupportedFormats.IsSupported(path))
            {
                target = path;
                continue;
            }

            ignored.Add(path);
        }

        // Nothing displayable was named, but an unsupported file was: open it so the
        // window can explain the format instead of silently showing the empty state.
        if (target is null && firstExisting is not null)
        {
            target = firstExisting;
        }

        return new CommandLineOptions
        {
            ShowHelp = showHelp,
            ShowVersion = showVersion,
            Benchmark = benchmark,
            TargetPath = target,
            IgnoredPaths = ignored,
            UnknownOptions = unknown
        };
    }
}

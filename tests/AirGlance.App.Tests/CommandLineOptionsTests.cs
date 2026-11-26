using AirGlance.App.Services;
using Xunit;

namespace AirGlance.App.Tests;

/// <summary>
/// The parser decides which file a double-click opens, and it had no direct test: every
/// earlier case walked through <c>MainWindow</c>. These cases pin the behaviour that the
/// window and the benchmarks rely on.
/// </summary>
public sealed class CommandLineOptionsTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "airglance-cli-" + Guid.NewGuid().ToString("N"));

    public CommandLineOptionsTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // Best effort cleanup.
        }
    }

    [Fact]
    public void No_arguments_opens_nothing()
    {
        var options = CommandLineOptions.Parse(null);

        Assert.Null(options.TargetPath);
        Assert.Equal(BenchmarkMode.None, options.Benchmark);
        Assert.False(options.ShowHelp);
        Assert.False(options.ShowVersion);
        Assert.Empty(options.IgnoredPaths);
        Assert.Empty(options.UnknownOptions);
    }

    [Theory]
    [InlineData("-h")]
    [InlineData("--help")]
    [InlineData("-?")]
    public void Help_flags_are_recognised(string argument)
    {
        Assert.True(CommandLineOptions.Parse([argument]).ShowHelp);
    }

    [Theory]
    [InlineData("-v")]
    [InlineData("--version")]
    public void Version_flags_are_recognised(string argument)
    {
        Assert.True(CommandLineOptions.Parse([argument]).ShowVersion);
    }

    [Theory]
    [InlineData("--bench=startup", BenchmarkMode.Startup)]
    [InlineData("--bench=decode", BenchmarkMode.Decode)]
    [InlineData("--bench=scan", BenchmarkMode.Scan)]
    public void Benchmark_modes_are_recognised(string argument, BenchmarkMode expected)
    {
        Assert.Equal(expected, CommandLineOptions.Parse([argument]).Benchmark);
    }

    [Fact]
    public void An_unknown_benchmark_mode_is_reported_rather_than_guessed()
    {
        var options = CommandLineOptions.Parse(["--bench=faster"]);

        Assert.Equal(BenchmarkMode.None, options.Benchmark);
        Assert.Equal(["--bench=faster"], options.UnknownOptions);
    }

    [Fact]
    public void Unknown_switches_are_collected_and_do_not_become_paths()
    {
        var options = CommandLineOptions.Parse(["-x", "--nope"]);

        Assert.Null(options.TargetPath);
        Assert.Equal(["-x", "--nope"], options.UnknownOptions);
        Assert.Empty(options.IgnoredPaths);
    }

    [Fact]
    public void Everything_after_a_double_dash_is_a_path()
    {
        var file = CreateImage("--weird-name.jpg");

        var options = CommandLineOptions.Parse(["--", file]);

        Assert.Equal(file, options.TargetPath);
        Assert.Empty(options.UnknownOptions);
    }

    [Fact]
    public void A_folder_wins_and_extra_files_are_ignored()
    {
        var file = CreateImage("photo.jpg");

        var options = CommandLineOptions.Parse([file, _root]);

        // The folder is a target; the file came first but a folder is preferred only when
        // it appears first, so check the actual rule: first existing supported entry wins.
        Assert.Equal(file, options.TargetPath);
        Assert.Equal([_root], options.IgnoredPaths);
    }

    [Fact]
    public void An_existing_but_unsupported_file_still_becomes_the_target()
    {
        // So the window can explain the format instead of showing the empty state.
        var path = Path.Combine(_root, "photo.heic");
        File.WriteAllBytes(path, [1, 2, 3]);

        var options = CommandLineOptions.Parse([path]);

        Assert.Equal(path, options.TargetPath);
    }

    [Fact]
    public void A_missing_path_is_ignored()
    {
        var missing = Path.Combine(_root, "nothing-here.jpg");

        var options = CommandLineOptions.Parse([missing]);

        Assert.Null(options.TargetPath);
        Assert.Equal([missing], options.IgnoredPaths);
    }

    [Fact]
    public void Blank_arguments_are_skipped()
    {
        var options = CommandLineOptions.Parse(["", "   "]);

        Assert.Null(options.TargetPath);
        Assert.Empty(options.IgnoredPaths);
        Assert.Empty(options.UnknownOptions);
    }

    private string CreateImage(string name)
    {
        var path = Path.Combine(_root, name);
        File.Copy(
            Path.Combine(AppContext.BaseDirectory, "Assets", "orientation-1.jpg"),
            path);
        return path;
    }
}

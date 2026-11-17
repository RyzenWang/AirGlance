using AirGlance.App.Services;
using AirGlance.App.Views;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Xunit;

namespace AirGlance.App.Tests;

public sealed class DialogChromeTests
{
    [AvaloniaFact]
    public void Settings_dialog_uses_the_shell_chrome()
    {
        var root = Path.Combine(Path.GetTempPath(), "airglance-dialog-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var services = new AppServices(
                Path.Combine(root, "settings"),
                Path.Combine(root, "cache"));
            var window = new SettingsWindow(services);

            if (OperatingSystem.IsWindows())
            {
                Assert.Equal(SystemDecorations.BorderOnly, window.SystemDecorations);
                Assert.True(window.ExtendClientAreaToDecorationsHint);
                Assert.True(window.HeaderCloseButton.IsVisible);
            }
            else
            {
                Assert.False(window.HeaderCloseButton.IsVisible);
            }
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
                // Best effort cleanup.
            }
        }
    }
}

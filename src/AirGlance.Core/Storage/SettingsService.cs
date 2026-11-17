using System.Text;
using System.Text.Json;

namespace AirGlance.Core.Storage;

/// <summary>
/// Small JSON settings store. Reading never throws; a missing or broken file means
/// defaults. Writing uses a temporary file and a replace, with an in-place fallback for
/// packaged builds where a rename can be redirected.
/// </summary>
public sealed class SettingsService
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
    private readonly object _gate = new();

    public SettingsService(string? settingsDirectory = null)
    {
        SettingsDirectory = settingsDirectory ?? AppPaths.SettingsDirectory;
        SettingsPath = Path.Combine(SettingsDirectory, "settings.json");
    }

    public string SettingsDirectory { get; }

    public string SettingsPath { get; }

    public string BackupPath => SettingsPath + ".bak";

    public AppSettings Load()
    {
        lock (_gate)
        {
            return Read(SettingsPath) ?? Read(BackupPath) ?? new AppSettings().Normalize();
        }
    }

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var settings = await ReadAsync(SettingsPath, cancellationToken).ConfigureAwait(false)
                ?? await ReadAsync(BackupPath, cancellationToken).ConfigureAwait(false);

            return settings ?? new AppSettings().Normalize();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return new AppSettings().Normalize();
        }
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        lock (_gate)
        {
            try
            {
                settings.Normalize();
                Directory.CreateDirectory(SettingsDirectory);
                var json = JsonSerializer.Serialize(settings, SettingsJsonContext.Default.AppSettings);
                Write(json);
            }
            catch
            {
                // Preferences must never keep the viewer from working.
            }
        }
    }

    private void Write(string json)
    {
        var existed = File.Exists(SettingsPath);
        if (existed)
        {
            try
            {
                File.Copy(SettingsPath, BackupPath, overwrite: true);
            }
            catch
            {
                // A missing backup only weakens crash recovery.
            }
        }

        var temporaryPath = SettingsPath + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, json, Utf8NoBom);
            File.Move(temporaryPath, SettingsPath, overwrite: true);
        }
        catch (IOException) when (existed)
        {
            // MSIX AppData redirection can refuse a rename across the real and virtual
            // locations. Overwrite the existing file instead; that stays one file.
            WriteInPlace(json);
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
            catch
            {
                // Best effort only.
            }
        }
    }

    private void WriteInPlace(string json)
    {
        using var stream = new FileStream(
            SettingsPath,
            FileMode.Open,
            FileAccess.Write,
            FileShare.None);
        var bytes = Utf8NoBom.GetBytes(json);
        stream.SetLength(0);
        stream.Write(bytes, 0, bytes.Length);
        stream.Flush(flushToDisk: true);
    }

    private static AppSettings? Read(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var json = File.ReadAllText(path);
            return Deserialize(json);
        }
        catch
        {
            return null;
        }
    }

    private static async Task<AppSettings?> ReadAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            return Deserialize(json);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private static AppSettings? Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        var settings = JsonSerializer.Deserialize(json, SettingsJsonContext.Default.AppSettings);
        return settings?.Normalize();
    }
}

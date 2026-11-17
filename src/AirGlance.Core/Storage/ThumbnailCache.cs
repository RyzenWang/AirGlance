using System.Security.Cryptography;
using System.Text;

namespace AirGlance.Core.Storage;

/// <summary>
/// Disk cache for 160 px filmstrip thumbnails. Entries are PNG files named after the
/// image's path, size and timestamp, so a changed file gets a new entry. Old entries are
/// removed after 30 days and the settings dialog can empty the folder.
/// </summary>
public sealed class ThumbnailCache
{
    private readonly object _gate = new();

    public ThumbnailCache(string? directory = null)
    {
        DirectoryPath = directory ?? AppPaths.ThumbnailDirectory;
    }

    public string DirectoryPath { get; }

    public bool TryRead(string imagePath, DateTime lastWriteUtc, long fileLength, out byte[] png)
    {
        png = [];
        var path = EntryPath(imagePath, lastWriteUtc, fileLength);

        try
        {
            lock (_gate)
            {
                if (!File.Exists(path))
                {
                    return false;
                }

                png = File.ReadAllBytes(path);
                return png.Length > 0;
            }
        }
        catch
        {
            return false;
        }
    }

    public void Write(string imagePath, DateTime lastWriteUtc, long fileLength, byte[] png)
    {
        if (png.Length == 0)
        {
            return;
        }

        var path = EntryPath(imagePath, lastWriteUtc, fileLength);
        var temporary = path + ".tmp";

        try
        {
            lock (_gate)
            {
                Directory.CreateDirectory(DirectoryPath);
                File.WriteAllBytes(temporary, png);
                File.Move(temporary, path, overwrite: true);
            }
        }
        catch
        {
            try
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
            catch
            {
                // Best effort only.
            }
        }
    }

    /// <summary>Deletes entries that have not been written for <paramref name="maxAge"/>.</summary>
    public int Cleanup(TimeSpan maxAge)
    {
        var cutoff = DateTime.UtcNow - maxAge;
        var deleted = 0;

        try
        {
            lock (_gate)
            {
                if (!Directory.Exists(DirectoryPath))
                {
                    return 0;
                }

                foreach (var file in Directory.EnumerateFiles(DirectoryPath, "*.png"))
                {
                    try
                    {
                        if (File.GetLastWriteTimeUtc(file) < cutoff)
                        {
                            File.Delete(file);
                            deleted++;
                        }
                    }
                    catch
                    {
                        // A file in use stays for the next run.
                    }
                }
            }
        }
        catch
        {
            // A missing cache is not an error.
        }

        return deleted;
    }

    public int Clear()
    {
        var deleted = 0;

        try
        {
            lock (_gate)
            {
                if (!Directory.Exists(DirectoryPath))
                {
                    return 0;
                }

                foreach (var file in Directory.EnumerateFiles(DirectoryPath))
                {
                    try
                    {
                        File.Delete(file);
                        deleted++;
                    }
                    catch
                    {
                        // Best effort only.
                    }
                }
            }
        }
        catch
        {
            // Best effort only.
        }

        return deleted;
    }

    public long GetSizeBytes()
    {
        long total = 0;

        try
        {
            lock (_gate)
            {
                if (!Directory.Exists(DirectoryPath))
                {
                    return 0;
                }

                foreach (var file in Directory.EnumerateFiles(DirectoryPath))
                {
                    try
                    {
                        total += new FileInfo(file).Length;
                    }
                    catch
                    {
                        // A vanished file contributes nothing.
                    }
                }
            }
        }
        catch
        {
            // Best effort only.
        }

        return total;
    }

    private string EntryPath(string imagePath, DateTime lastWriteUtc, long fileLength)
    {
        var key = $"{Path.GetFullPath(imagePath)}|{lastWriteUtc.Ticks}|{fileLength}|160";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return Path.Combine(DirectoryPath, Convert.ToHexString(hash)[..40] + ".png");
    }
}

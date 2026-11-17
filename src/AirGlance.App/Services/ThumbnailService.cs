using System.IO;
using AirGlance.App.ViewModels;
using AirGlance.Core.Imaging;
using AirGlance.Core.Storage;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace AirGlance.App.Services;

/// <summary>
/// Generates filmstrip thumbnails on one background thread, in folder order. Failures
/// are not retried. Disk-cached PNGs from earlier runs are used first.
/// </summary>
public sealed class ThumbnailService : IDisposable
{
    private const int ThumbnailSize = 160;
    private readonly ThumbnailCache _cache;
    private CancellationTokenSource? _cancellation;
    private bool _disposed;

    public ThumbnailService(ThumbnailCache cache)
    {
        _cache = cache;
    }

    public void Start(IEnumerable<ImageItemViewModel> items, CancellationToken cancellationToken)
    {
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var list = items.ToList();
        _ = Task.Run(() => RunAsync(list, _cancellation.Token), CancellationToken.None);
    }

    public void Stop()
    {
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _cancellation = null;
    }

    private async Task RunAsync(List<ImageItemViewModel> items, CancellationToken cancellationToken)
    {
        foreach (var item in items)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            try
            {
                var png = await Task.Run(() => LoadThumbnail(item.Path, cancellationToken), cancellationToken)
                    .ConfigureAwait(false);
                if (png is null)
                {
                    continue;
                }

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }

                    try
                    {
                        using var stream = new MemoryStream(png);
                        item.Thumbnail = new Bitmap(stream);
                    }
                    catch
                    {
                        item.ThumbnailFailed = true;
                    }
                });
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                await Dispatcher.UIThread.InvokeAsync(() => item.ThumbnailFailed = true);
            }
        }
    }

    private byte[]? LoadThumbnail(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        DateTime lastWrite;
        long length;

        try
        {
            var info = new FileInfo(path);
            lastWrite = info.LastWriteTimeUtc;
            length = info.Length;
        }
        catch
        {
            return null;
        }

        if (_cache.TryRead(path, lastWrite, length, out var cached))
        {
            return cached;
        }

        var decoded = ImageDecoder.Decode(path, ThumbnailSize, ThumbnailSize, cancellationToken);
        var png = ImageExporter.Encode(decoded, ExportFormat.Png);
        _cache.Write(path, lastWrite, length, png);
        return png;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
    }
}

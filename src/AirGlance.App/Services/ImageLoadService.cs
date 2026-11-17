using AirGlance.Core.Imaging;

namespace AirGlance.App.Services;

/// <summary>
/// A tiny in-memory cache for screen-sized previews of the current, previous and next
/// images. Switching to a prefetched neighbour then costs a bitmap copy, not a decode.
/// </summary>
public sealed class ImageLoadService
{
    private const int Capacity = 4;
    private readonly object _gate = new();
    private readonly Dictionary<string, LinkedListNode<Entry>> _entries = new(StringComparer.Ordinal);
    private readonly LinkedList<Entry> _order = new();

    public bool TryGet(string path, int maxWidth, int maxHeight, out DecodedBitmap bitmap)
    {
        var key = Key(path, maxWidth, maxHeight);

        lock (_gate)
        {
            if (_entries.TryGetValue(key, out var node))
            {
                _order.Remove(node);
                _order.AddFirst(node);
                bitmap = node.Value.Bitmap;
                return true;
            }
        }

        bitmap = null!;
        return false;
    }

    public void Store(string path, int maxWidth, int maxHeight, DecodedBitmap bitmap)
    {
        var key = Key(path, maxWidth, maxHeight);

        lock (_gate)
        {
            if (_entries.TryGetValue(key, out var existing))
            {
                _order.Remove(existing);
                _entries.Remove(key);
            }

            var node = new LinkedListNode<Entry>(new Entry(key, bitmap));
            _order.AddFirst(node);
            _entries[key] = node;

            while (_order.Count > Capacity)
            {
                var last = _order.Last!;
                _order.RemoveLast();
                _entries.Remove(last.Value.Key);
            }
        }
    }

    public Task<DecodedBitmap> DecodePreviewAsync(
        string path,
        int maxWidth,
        int maxHeight,
        CancellationToken cancellationToken) =>
        Task.Run(() => ImageDecoder.Decode(path, maxWidth, maxHeight, cancellationToken), cancellationToken);

    public Task<DecodedBitmap> DecodeFullAsync(string path, CancellationToken cancellationToken) =>
        Task.Run(() => ImageDecoder.DecodeFull(path, cancellationToken), cancellationToken);

    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
            _order.Clear();
        }
    }

    private static string Key(string path, int maxWidth, int maxHeight) =>
        string.Concat(path, "|", maxWidth.ToString(System.Globalization.CultureInfo.InvariantCulture), "x", maxHeight.ToString(System.Globalization.CultureInfo.InvariantCulture));

    private readonly record struct Entry(string Key, DecodedBitmap Bitmap);
}

using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ImageLightBox.Wpf;

internal readonly record struct ImageRequest(string Path, int Size);

/// <summary>
/// Decodes images in the background and keeps the most recently used in memory. Each request
/// replaces the queue, so flipping fast through pages doesn't work through the ones already left
/// behind, while what was decoded stays cached and paging back is instant.
/// </summary>
internal sealed class ThumbnailLoader(Dispatcher dispatcher)
{
    private const long MaxCachedBytes = 1024L * 1024 * 1024;
    private static readonly int MaxWorkers = Math.Clamp(Environment.ProcessorCount / 2, 2, 6);

    // A null image means it can't be decoded, which is remembered so it isn't tried again.
    private sealed record CacheEntry(ImageRequest Request, BitmapSource? Image, long Bytes);

    private readonly object _lock = new();
    private readonly Dictionary<ImageRequest, LinkedListNode<CacheEntry>> _cache = [];
    private readonly LinkedList<CacheEntry> _recent = [];
    private readonly HashSet<ImageRequest> _running = [];
    private readonly Dictionary<string, HashSet<int>> _sizesByPath = [];
    private List<ImageRequest> _queue = [];
    private long _cachedBytes;
    private int _workers;

    /// <summary>Raised on the UI thread. The image is null if the file can't be decoded.</summary>
    public event Action<ImageRequest, BitmapSource?>? Loaded;

    /// <summary>Replaces the queue. The first requests are decoded first.</summary>
    public void Request(IEnumerable<ImageRequest> requests)
    {
        lock (_lock)
        {
            _queue = requests.Distinct().Where(r => !_cache.ContainsKey(r) && !_running.Contains(r)).ToList();
            for (; _workers < MaxWorkers && _workers < _queue.Count; _workers++)
                Task.Run(Work);
        }
    }

    /// <summary>The cached image at this size or else the closest there is, to show while the right one loads.</summary>
    public BitmapSource? GetBest(string path, int size, out bool failed)
    {
        lock (_lock)
        {
            failed = false;
            if (_cache.TryGetValue(new(path, size), out var exact))
            {
                _recent.Remove(exact);
                _recent.AddFirst(exact);
                failed = exact.Value.Image is null;
                return exact.Value.Image;
            }

            if (!_sizesByPath.TryGetValue(path, out var sizes))
                return null;
            return sizes
                .OrderBy(s => Math.Abs(s - size))
                .Select(s => _cache[new(path, s)].Value.Image)
                .FirstOrDefault(image => image is not null);
        }
    }

    /// <summary>Drops a file that has been deleted or moved.</summary>
    public void Forget(string path)
    {
        lock (_lock)
        {
            if (!_sizesByPath.TryGetValue(path, out var sizes))
                return;
            foreach (var size in sizes.ToArray())
                Remove(_cache[new(path, size)]);
        }
    }

    private void Work()
    {
        while (true)
        {
            ImageRequest request;
            lock (_lock)
            {
                if (_queue.Count == 0)
                {
                    _workers--;
                    return;
                }
                request = _queue[0];
                _queue.RemoveAt(0);
                _running.Add(request);
            }

            BitmapSource? image = null;
            try
            {
                image = ImageDecoder.Decode(request.Path, request.Size);
            }
            catch (Exception)
            {
                // Damaged or gone: the tile says it can't be shown.
            }

            lock (_lock)
            {
                _running.Remove(request);
                Store(request, image);
            }
            dispatcher.BeginInvoke(() => Loaded?.Invoke(request, image));
        }
    }

    private void Store(ImageRequest request, BitmapSource? image)
    {
        var bytes = image is null ? 0L : (long)image.PixelWidth * image.PixelHeight * image.Format.BitsPerPixel / 8;
        _cache[request] = _recent.AddFirst(new CacheEntry(request, image, bytes));
        _cachedBytes += bytes;

        if (!_sizesByPath.TryGetValue(request.Path, out var sizes))
            _sizesByPath[request.Path] = sizes = [];
        sizes.Add(request.Size);

        while (_cachedBytes > MaxCachedBytes && _recent.Last is { } oldest)
            Remove(oldest);
    }

    private void Remove(LinkedListNode<CacheEntry> node)
    {
        var (request, _, bytes) = node.Value;
        _recent.Remove(node);
        _cache.Remove(request);
        _cachedBytes -= bytes;

        var sizes = _sizesByPath[request.Path];
        sizes.Remove(request.Size);
        if (sizes.Count == 0)
            _sizesByPath.Remove(request.Path);
    }
}

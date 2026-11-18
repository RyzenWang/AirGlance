using Avalonia.Media.Imaging;

namespace AirGlance.App.Imaging;

/// <summary>
/// UI-thread bitmaps for an animated GIF. The view model owns this object; the
/// viewer borrows the frames and stops its timer before the object is disposed.
/// </summary>
public sealed class AnimatedImage : IDisposable
{
    public AnimatedImage(
        IReadOnlyList<WriteableBitmap> frames,
        IReadOnlyList<int> delaysMilliseconds,
        int sourceWidth,
        int sourceHeight,
        double scale,
        int repetitionCount)
    {
        ArgumentNullException.ThrowIfNull(frames);
        ArgumentNullException.ThrowIfNull(delaysMilliseconds);
        if (frames.Count == 0 || frames.Count != delaysMilliseconds.Count)
        {
            throw new ArgumentException("Animation frames and delays must be non-empty and aligned.");
        }

        Frames = frames;
        DelaysMilliseconds = delaysMilliseconds;
        SourceWidth = sourceWidth;
        SourceHeight = sourceHeight;
        Scale = scale;
        RepetitionCount = repetitionCount;
    }

    public IReadOnlyList<WriteableBitmap> Frames { get; }

    public int FrameCount => Frames.Count;

    public IReadOnlyList<int> DelaysMilliseconds { get; }

    public int SourceWidth { get; }

    public int SourceHeight { get; }

    public double Scale { get; }

    public int RepetitionCount { get; }

    public void Dispose()
    {
        foreach (var frame in Frames)
        {
            frame.Dispose();
        }
    }
}

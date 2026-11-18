namespace AirGlance.Core.Imaging;

/// <summary>
/// A decoded animated GIF. Every frame is already composited to the same canvas and
/// carries its own delay; the UI owns only the timer and bitmap conversion.
/// </summary>
public sealed class DecodedAnimation
{
    public DecodedAnimation(
        IReadOnlyList<DecodedBitmap> frames,
        IReadOnlyList<int> delaysMilliseconds,
        int repetitionCount)
    {
        ArgumentNullException.ThrowIfNull(frames);
        ArgumentNullException.ThrowIfNull(delaysMilliseconds);
        if (frames.Count == 0)
        {
            throw new ArgumentException("An animation needs at least one frame.", nameof(frames));
        }

        if (frames.Count != delaysMilliseconds.Count)
        {
            throw new ArgumentException("Every frame needs a delay.", nameof(delaysMilliseconds));
        }

        Frames = frames;
        DelaysMilliseconds = delaysMilliseconds;
        RepetitionCount = repetitionCount;
    }

    public IReadOnlyList<DecodedBitmap> Frames { get; }

    public int FrameCount => Frames.Count;

    public IReadOnlyList<int> DelaysMilliseconds { get; }

    /// <summary>-1 means loop forever, matching SKCodec's GIF convention.</summary>
    public int RepetitionCount { get; }
}

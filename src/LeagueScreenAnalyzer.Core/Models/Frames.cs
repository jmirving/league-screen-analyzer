namespace LeagueScreenAnalyzer.Core.Models;

public interface IFramePayload;

public sealed record SourceFrame
{
    public SourceFrame(long sequenceNumber, TimeSpan sourceTimestamp, int width, int height, IFramePayload payload)
    {
        if (sequenceNumber < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sequenceNumber), sequenceNumber, "Sequence number cannot be negative.");
        }

        if (sourceTimestamp < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceTimestamp), sourceTimestamp, "Source timestamp cannot be negative.");
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        SequenceNumber = sequenceNumber;
        SourceTimestamp = sourceTimestamp;
        Width = width;
        Height = height;
        Payload = payload ?? throw new ArgumentNullException(nameof(payload));
    }

    public long SequenceNumber { get; }

    public TimeSpan SourceTimestamp { get; }

    public int Width { get; }

    public int Height { get; }

    public IFramePayload Payload { get; }
}

public sealed record RegionFrame
{
    public RegionFrame(
        RegionType regionType,
        long sourceFrameSequence,
        TimeSpan sourceTimestamp,
        int width,
        int height,
        IFramePayload payload)
    {
        if (!Enum.IsDefined(regionType))
        {
            throw new ArgumentOutOfRangeException(nameof(regionType));
        }

        if (sourceFrameSequence < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceFrameSequence));
        }

        if (sourceTimestamp < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceTimestamp));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        RegionType = regionType;
        SourceFrameSequence = sourceFrameSequence;
        SourceTimestamp = sourceTimestamp;
        Width = width;
        Height = height;
        Payload = payload ?? throw new ArgumentNullException(nameof(payload));
    }

    public RegionType RegionType { get; }

    public long SourceFrameSequence { get; }

    public TimeSpan SourceTimestamp { get; }

    public int Width { get; }

    public int Height { get; }

    public IFramePayload Payload { get; }
}

public sealed record ExtractedRegions(RegionFrame Clock, RegionFrame Minimap);

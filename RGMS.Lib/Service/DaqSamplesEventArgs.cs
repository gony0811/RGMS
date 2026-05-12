namespace RGMS.Lib.Service;

public sealed class DaqSamplesEventArgs : EventArgs
{
    public required DateTimeOffset TimestampUtc { get; init; }
    public required double SampleRateHz { get; init; }
    public required IReadOnlyList<string> ChannelNames { get; init; }
    public required double[,] Samples { get; init; }
    public required long ChunkSequence { get; init; }
}

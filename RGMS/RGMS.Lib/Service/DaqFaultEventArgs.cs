namespace RGMS.Lib.Service;

public sealed class DaqFaultEventArgs : EventArgs
{
    public required Exception Exception { get; init; }
    public required bool IsFatal { get; init; }
}

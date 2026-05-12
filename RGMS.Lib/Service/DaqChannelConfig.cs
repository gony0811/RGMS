namespace RGMS.Lib.Service;

public sealed record DaqChannelConfig
{
    public required string PhysicalChannel { get; init; }
    public string? Name { get; init; }
    public DaqTerminalConfig Terminal { get; init; } = DaqTerminalConfig.Rse;
    public double MinVolts { get; init; } = -10.0;
    public double MaxVolts { get; init; } = +10.0;
}

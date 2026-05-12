namespace RGMS.Lib.Service;

public sealed record PhaseConfiguration
{
    public double GateOnPhaseDeg { get; set; } = -45.0;
    public double GateOffPhaseDeg { get; set; } = 45.0;

    public static PhaseConfiguration Default() => new();
}

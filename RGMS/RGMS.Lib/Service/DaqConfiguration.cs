namespace RGMS.Lib.Service;

public sealed record DaqConfiguration
{
    public string DeviceName { get; set; } = "Dev1";
    public IReadOnlyList<DaqChannelConfig> Channels { get; set; } = Array.Empty<DaqChannelConfig>();
    public double SampleRateHz { get; set; } = 500.0;
    public int SamplesPerChannelPerCallback { get; set; } = 50;

    public static DaqConfiguration DefaultRgms() => new()
    {
        DeviceName = "Dev1",
        SampleRateHz = 500.0,
        SamplesPerChannelPerCallback = 50,
        Channels = new[]
        {
            new DaqChannelConfig
            {
                PhysicalChannel = "Dev1/ai0",
                Name = "Photodiode",
                Terminal = DaqTerminalConfig.Rse,
                MinVolts = -10.0,
                MaxVolts = +10.0,
            },
            new DaqChannelConfig
            {
                PhysicalChannel = "Dev1/ai1",
                Name = "LaserDistance",
                Terminal = DaqTerminalConfig.Rse,
                MinVolts = -10.0,
                MaxVolts = +10.0,
            },
        },
    };
}

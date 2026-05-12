using System.ComponentModel.DataAnnotations;

namespace RGMS.Lib.Data.Entities;

public class GeneralSettingsEntity
{
    public int Id { get; set; }

    [Required]
    public string DeviceName { get; set; } = "Dev1";

    public double SampleRateHz { get; set; } = 500.0;

    public int SamplesPerChannelPerCallback { get; set; } = 50;

    public double GateOnPhaseDeg { get; set; } = -45.0;

    public double GateOffPhaseDeg { get; set; } = 45.0;

    public List<DaqChannelSettingEntity> Channels { get; set; } = new();
}

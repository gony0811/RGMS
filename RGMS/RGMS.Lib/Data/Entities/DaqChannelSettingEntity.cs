using System.ComponentModel.DataAnnotations;
using RGMS.Lib.Service;

namespace RGMS.Lib.Data.Entities;

public class DaqChannelSettingEntity
{
    public int Id { get; set; }

    public int GeneralSettingsId { get; set; }

    public int ChannelIndex { get; set; }

    [Required]
    public string PhysicalChannel { get; set; } = string.Empty;

    public string? Name { get; set; }

    public DaqTerminalConfig Terminal { get; set; } = DaqTerminalConfig.Rse;

    public double MinVolts { get; set; } = -10.0;

    public double MaxVolts { get; set; } = 10.0;

    public GeneralSettingsEntity? GeneralSettings { get; set; }
}

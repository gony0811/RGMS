using RGMS.Lib.Service;

namespace RGMS.Lib.Data;

public interface ISettingsStore
{
    Task<(DaqConfiguration Daq, PhaseConfiguration Phase)> LoadAsync(CancellationToken ct = default);

    Task SaveAsync(DaqConfiguration daq, PhaseConfiguration phase, CancellationToken ct = default);

    event EventHandler? Changed;
}

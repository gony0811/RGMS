using Microsoft.EntityFrameworkCore;
using RGMS.Lib.Data.Entities;
using RGMS.Lib.Service;

namespace RGMS.Lib.Data;

public sealed class SettingsStore : ISettingsStore
{
    private readonly IDbContextFactory<RgmsDbContext> _factory;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private (DaqConfiguration Daq, PhaseConfiguration Phase)? _cache;

    public event EventHandler? Changed;

    public SettingsStore(IDbContextFactory<RgmsDbContext> factory)
    {
        _factory = factory;
    }

    public async Task<(DaqConfiguration Daq, PhaseConfiguration Phase)> LoadAsync(CancellationToken ct = default)
    {
        if (_cache is { } cached) return cached;

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_cache is { } c) return c;

            await using var db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);
            var general = await db.GeneralSettings
                .Include(g => g.Channels)
                .OrderBy(g => g.Id)
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);

            if (general is null)
            {
                var (seedDaq, seedPhase) = SeedDefaults();
                general = ToEntity(seedDaq, seedPhase);
                db.GeneralSettings.Add(general);
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }

            _cache = (ToDaqConfiguration(general), ToPhaseConfiguration(general));
            return _cache.Value;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(DaqConfiguration daq, PhaseConfiguration phase, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(daq);
        ArgumentNullException.ThrowIfNull(phase);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);
            var general = await db.GeneralSettings
                .Include(g => g.Channels)
                .OrderBy(g => g.Id)
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);

            if (general is null)
            {
                general = ToEntity(daq, phase);
                db.GeneralSettings.Add(general);
            }
            else
            {
                general.DeviceName = daq.DeviceName;
                general.SampleRateHz = daq.SampleRateHz;
                general.SamplesPerChannelPerCallback = daq.SamplesPerChannelPerCallback;
                general.GateOnPhaseDeg = phase.GateOnPhaseDeg;
                general.GateOffPhaseDeg = phase.GateOffPhaseDeg;

                db.DaqChannelSettings.RemoveRange(general.Channels);
                general.Channels = MapChannels(daq.Channels);
            }

            await db.SaveChangesAsync(ct).ConfigureAwait(false);

            _cache = (ToDaqConfiguration(general), ToPhaseConfiguration(general));
        }
        finally
        {
            _gate.Release();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static (DaqConfiguration Daq, PhaseConfiguration Phase) SeedDefaults()
        => (DaqConfiguration.DefaultRgms(), PhaseConfiguration.Default());

    private static GeneralSettingsEntity ToEntity(DaqConfiguration daq, PhaseConfiguration phase) => new()
    {
        DeviceName = daq.DeviceName,
        SampleRateHz = daq.SampleRateHz,
        SamplesPerChannelPerCallback = daq.SamplesPerChannelPerCallback,
        GateOnPhaseDeg = phase.GateOnPhaseDeg,
        GateOffPhaseDeg = phase.GateOffPhaseDeg,
        Channels = MapChannels(daq.Channels),
    };

    private static List<DaqChannelSettingEntity> MapChannels(IReadOnlyList<DaqChannelConfig> channels)
    {
        var list = new List<DaqChannelSettingEntity>(channels.Count);
        for (var i = 0; i < channels.Count; i++)
        {
            var src = channels[i];
            list.Add(new DaqChannelSettingEntity
            {
                ChannelIndex = i,
                PhysicalChannel = src.PhysicalChannel,
                Name = src.Name,
                Terminal = src.Terminal,
                MinVolts = src.MinVolts,
                MaxVolts = src.MaxVolts,
            });
        }
        return list;
    }

    private static DaqConfiguration ToDaqConfiguration(GeneralSettingsEntity general)
    {
        var channels = general.Channels
            .OrderBy(c => c.ChannelIndex)
            .Select(c => new DaqChannelConfig
            {
                PhysicalChannel = c.PhysicalChannel,
                Name = c.Name,
                Terminal = c.Terminal,
                MinVolts = c.MinVolts,
                MaxVolts = c.MaxVolts,
            })
            .ToArray();

        return new DaqConfiguration
        {
            DeviceName = general.DeviceName,
            SampleRateHz = general.SampleRateHz,
            SamplesPerChannelPerCallback = general.SamplesPerChannelPerCallback,
            Channels = channels,
        };
    }

    private static PhaseConfiguration ToPhaseConfiguration(GeneralSettingsEntity general) => new()
    {
        GateOnPhaseDeg = general.GateOnPhaseDeg,
        GateOffPhaseDeg = general.GateOffPhaseDeg,
    };
}

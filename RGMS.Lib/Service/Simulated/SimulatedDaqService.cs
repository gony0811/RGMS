using Microsoft.Extensions.Logging;

namespace RGMS.Lib.Service.Simulated;

public sealed class SimulatedDaqService : IDaqService
{
    private readonly ILogger<SimulatedDaqService> _logger;
    private readonly object _gate = new();
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private long _chunkSeq;
    private DaqConfiguration? _config;
    private DaqServiceState _state = DaqServiceState.Idle;

    public DaqServiceState State => _state;
    public DaqConfiguration? Configuration => _config;

    public event EventHandler<DaqSamplesEventArgs>? SamplesAcquired;
    public event EventHandler<DaqFaultEventArgs>? AcquisitionFaulted;
    public event EventHandler<DaqServiceState>? StateChanged;

    public SimulatedDaqService(ILogger<SimulatedDaqService> logger)
    {
        _logger = logger;
    }

    public Task StartAsync(DaqConfiguration config, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (config.Channels.Count == 0)
            throw new ArgumentException("At least one channel must be configured.", nameof(config));
        if (config.SampleRateHz <= 0)
            throw new ArgumentOutOfRangeException(nameof(config), "SampleRateHz must be positive.");
        if (config.SamplesPerChannelPerCallback <= 0)
            throw new ArgumentOutOfRangeException(nameof(config), "SamplesPerChannelPerCallback must be positive.");

        lock (_gate)
        {
            if (_state is DaqServiceState.Running or DaqServiceState.Starting)
                throw new InvalidOperationException("Acquisition is already running.");

            TransitionTo(DaqServiceState.Starting);
            _config = config;
            _chunkSeq = 0;
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _loop = Task.Run(() => RunLoopAsync(token), token);
            TransitionTo(DaqServiceState.Running);
        }

        _logger.LogInformation("Simulated DAQ started: {Channels} ch @ {Rate} Hz",
            config.Channels.Count, config.SampleRateHz);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        Task? loop;
        CancellationTokenSource? cts;
        lock (_gate)
        {
            if (_state is DaqServiceState.Idle or DaqServiceState.Stopping)
                return;
            TransitionTo(DaqServiceState.Stopping);
            loop = _loop;
            cts = _cts;
            _loop = null;
            _cts = null;
        }

        cts?.Cancel();
        if (loop is not null)
        {
            try { await loop.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
        cts?.Dispose();

        lock (_gate)
        {
            TransitionTo(DaqServiceState.Idle);
        }
        _logger.LogInformation("Simulated DAQ stopped.");
    }

    public Task<double[,]> ReadOnceAsync(DaqConfiguration config, int samplesPerChannel, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (samplesPerChannel <= 0)
            throw new ArgumentOutOfRangeException(nameof(samplesPerChannel));
        if (config.Channels.Count == 0)
            throw new ArgumentException("At least one channel must be configured.", nameof(config));

        cancellationToken.ThrowIfCancellationRequested();
        var data = GenerateChunk(config, chunkIndex: 0, samplesPerChannel);
        return Task.FromResult(data);
    }

    public Task<IReadOnlyList<DaqDeviceInfo>> EnumerateDevicesAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<DaqDeviceInfo> list = new[]
        {
            new DaqDeviceInfo(
                Name: "Sim1",
                ProductType: "USB-6001 (Simulated)",
                SerialNumber: "SIM-0001",
                AnalogInputChannels: new[] { "Sim1/ai0", "Sim1/ai1" },
                AnalogOutputChannels: Array.Empty<string>()),
        };
        return Task.FromResult(list);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        var cfg = _config!;
        var periodSeconds = cfg.SamplesPerChannelPerCallback / cfg.SampleRateHz;
        var period = TimeSpan.FromSeconds(periodSeconds);
        using var timer = new PeriodicTimer(period);

        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                var seq = Interlocked.Increment(ref _chunkSeq);
                var samples = GenerateChunk(cfg, seq - 1, cfg.SamplesPerChannelPerCallback);
                var names = cfg.Channels
                    .Select((c, i) => c.Name ?? c.PhysicalChannel)
                    .ToArray();

                try
                {
                    SamplesAcquired?.Invoke(this, new DaqSamplesEventArgs
                    {
                        TimestampUtc = DateTimeOffset.UtcNow,
                        SampleRateHz = cfg.SampleRateHz,
                        ChannelNames = names,
                        Samples = samples,
                        ChunkSequence = seq,
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Subscriber threw from SamplesAcquired.");
                    AcquisitionFaulted?.Invoke(this, new DaqFaultEventArgs { Exception = ex, IsFatal = false });
                }
            }
        }
        catch (OperationCanceledException) { /* normal stop */ }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Simulated DAQ loop faulted.");
            lock (_gate) { TransitionTo(DaqServiceState.Faulted); }
            AcquisitionFaulted?.Invoke(this, new DaqFaultEventArgs { Exception = ex, IsFatal = true });
        }
    }

    private static double[,] GenerateChunk(DaqConfiguration cfg, long chunkIndex, int samplesPerChannel)
    {
        var channelCount = cfg.Channels.Count;
        var data = new double[channelCount, samplesPerChannel];
        var dt = 1.0 / cfg.SampleRateHz;
        var t0 = chunkIndex * samplesPerChannel * dt;

        // Breathing-like waveform: 0.25 Hz fundamental, plus harmonic and noise.
        // AI0 (Photodiode-like): higher frequency 0.3 Hz sine, smaller amplitude.
        // AI1 (Laser distance-like): 0.25 Hz sine, larger amplitude, slight phase shift.
        var rng = new Random(unchecked((int)(chunkIndex * 9176 + 17)));

        for (int s = 0; s < samplesPerChannel; s++)
        {
            var t = t0 + s * dt;
            for (int c = 0; c < channelCount; c++)
            {
                double v;
                if (c == 0)
                {
                    v = 0.8 * Math.Sin(2 * Math.PI * 0.30 * t)
                        + 0.05 * Math.Sin(2 * Math.PI * 1.5 * t)
                        + (rng.NextDouble() - 0.5) * 0.02;
                }
                else if (c == 1)
                {
                    v = 2.5 * Math.Sin(2 * Math.PI * 0.25 * t + 0.4)
                        + 0.1 * Math.Sin(2 * Math.PI * 0.5 * t)
                        + (rng.NextDouble() - 0.5) * 0.01;
                }
                else
                {
                    v = Math.Sin(2 * Math.PI * 0.25 * t + c * 0.3)
                        + (rng.NextDouble() - 0.5) * 0.02;
                }

                var ch = cfg.Channels[c];
                if (v < ch.MinVolts) v = ch.MinVolts;
                else if (v > ch.MaxVolts) v = ch.MaxVolts;
                data[c, s] = v;
            }
        }

        return data;
    }

    private void TransitionTo(DaqServiceState next)
    {
        if (_state == next) return;
        _state = next;
        StateChanged?.Invoke(this, next);
    }
}

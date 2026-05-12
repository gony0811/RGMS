using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using NationalInstruments.DAQmx;
using NiTask = NationalInstruments.DAQmx.Task;
using Task = System.Threading.Tasks.Task;

namespace RGMS.Lib.Service;

[SupportedOSPlatform("windows")]
public sealed class DaqService : IDaqService
{
    private readonly ILogger<DaqService> _logger;
    private readonly object _gate = new();
    private NiTask? _task;
    private AnalogMultiChannelReader? _reader;
    private string[] _channelNames = Array.Empty<string>();
    private long _chunkSeq;
    private DaqConfiguration? _config;
    private DaqServiceState _state = DaqServiceState.Idle;

    public DaqServiceState State => _state;
    public DaqConfiguration? Configuration => _config;

    public event EventHandler<DaqSamplesEventArgs>? SamplesAcquired;
    public event EventHandler<DaqFaultEventArgs>? AcquisitionFaulted;
    public event EventHandler<DaqServiceState>? StateChanged;

    public DaqService(ILogger<DaqService> logger)
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
            try
            {
                BuildAndStartTask(config);
                _config = config;
                _chunkSeq = 0;
                TransitionTo(DaqServiceState.Running);
            }
            catch
            {
                DisposeTaskNoLock();
                TransitionTo(DaqServiceState.Faulted);
                throw;
            }
        }

        _logger.LogInformation("DAQ started: device={Device} channels={Channels} rate={Rate}Hz n={N}",
            config.DeviceName, config.Channels.Count, config.SampleRateHz, config.SamplesPerChannelPerCallback);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        NiTask? captured;
        lock (_gate)
        {
            if (_state is DaqServiceState.Idle or DaqServiceState.Stopping)
                return;
            TransitionTo(DaqServiceState.Stopping);
            // Detach the task/reader from the service *under the lock* so any racing
            // OnEveryNSamplesRead callback sees _task == null and bails out at its
            // entry guard. We then call task.Stop()/Dispose() OUTSIDE the lock and on
            // a worker thread — NI's task.Stop() blocks waiting for its callback
            // dispatch state to flush, and Blazor Server callbacks/events frequently
            // share the same ThreadPool, so calling Stop() from the dispatcher (or
            // while holding _gate) can pin the dispatcher and deadlock the UI.
            captured = _task;
            _task = null;
            _reader = null;
            _channelNames = Array.Empty<string>();
        }

        if (captured is not null)
        {
            await Task.Run(() =>
            {
                try { captured.EveryNSamplesRead -= OnEveryNSamplesRead; } catch { }
                try { captured.Stop(); } catch { }
                try { captured.Dispose(); } catch { }
            }, cancellationToken).ConfigureAwait(false);
        }

        lock (_gate)
        {
            TransitionTo(DaqServiceState.Idle);
        }
        _logger.LogInformation("DAQ stopped.");
    }

    public Task<double[,]> ReadOnceAsync(DaqConfiguration config, int samplesPerChannel, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (samplesPerChannel <= 0)
            throw new ArgumentOutOfRangeException(nameof(samplesPerChannel));
        if (config.Channels.Count == 0)
            throw new ArgumentException("At least one channel must be configured.", nameof(config));

        lock (_gate)
        {
            if (_state is DaqServiceState.Running or DaqServiceState.Starting)
                throw new InvalidOperationException("Cannot ReadOnce while continuous acquisition is running.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        using var task = new NiTask("rgms_ai_once");
        foreach (var ch in config.Channels)
        {
            task.AIChannels.CreateVoltageChannel(
                ch.PhysicalChannel,
                ch.Name ?? string.Empty,
                MapTerminal(ch.Terminal),
                ch.MinVolts,
                ch.MaxVolts,
                AIVoltageUnits.Volts);
        }
        task.Timing.ConfigureSampleClock(
            string.Empty,
            config.SampleRateHz,
            SampleClockActiveEdge.Rising,
            SampleQuantityMode.FiniteSamples,
            samplesPerChannel);

        task.Control(TaskAction.Verify);
        var reader = new AnalogMultiChannelReader(task.Stream);
        var data = reader.ReadMultiSample(samplesPerChannel);
        return Task.FromResult(data);
    }

    public Task<IReadOnlyList<DaqDeviceInfo>> EnumerateDevicesAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run<IReadOnlyList<DaqDeviceInfo>>(() =>
        {
            var devices = DaqSystem.Local.Devices;
            var result = new List<DaqDeviceInfo>(devices.Length);
            foreach (var name in devices)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var dev = DaqSystem.Local.LoadDevice(name);
                    result.Add(new DaqDeviceInfo(
                        Name: name,
                        ProductType: dev.ProductType ?? string.Empty,
                        SerialNumber: dev.SerialNumber.ToString("X"),
                        AnalogInputChannels: dev.AIPhysicalChannels ?? Array.Empty<string>(),
                        AnalogOutputChannels: dev.AOPhysicalChannels ?? Array.Empty<string>()));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to inspect device {Device}", name);
                }
            }
            return result;
        }, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
    }

    private void BuildAndStartTask(DaqConfiguration config)
    {
        var task = new NiTask("rgms_ai");
        foreach (var ch in config.Channels)
        {
            task.AIChannels.CreateVoltageChannel(
                ch.PhysicalChannel,
                ch.Name ?? string.Empty,
                MapTerminal(ch.Terminal),
                ch.MinVolts,
                ch.MaxVolts,
                AIVoltageUnits.Volts);
        }

        task.Timing.ConfigureSampleClock(
            string.Empty,
            config.SampleRateHz,
            SampleClockActiveEdge.Rising,
            SampleQuantityMode.ContinuousSamples,
            config.SamplesPerChannelPerCallback);

        task.Control(TaskAction.Verify);

        _channelNames = config.Channels
            .Select(c => c.Name ?? c.PhysicalChannel)
            .ToArray();

        var reader = new AnalogMultiChannelReader(task.Stream)
        {
            SynchronizeCallbacks = false,
        };

        task.EveryNSamplesReadEventInterval = config.SamplesPerChannelPerCallback;
        task.EveryNSamplesRead += OnEveryNSamplesRead;

        _task = task;
        _reader = reader;

        task.Start();
    }

    private void OnEveryNSamplesRead(object? sender, EveryNSamplesReadEventArgs e)
    {
        // NI-DAQmx may dispatch a callback to the managed event handler after we have
        // already unsubscribed and disposed the originating task. If a new Start has
        // raced in by then, this stale callback would use the new task's reader (or
        // a half-built one) and trigger a spurious DaqException that, in turn, would
        // fault the *new* run. Discard any callback whose source task is no longer
        // the active one.
        var currentTask = _task;
        if (currentTask is null || !ReferenceEquals(sender, currentTask))
            return;

        var reader = _reader;
        var cfg = _config;
        if (reader is null || cfg is null) return;

        try
        {
            var samples = reader.ReadMultiSample(cfg.SamplesPerChannelPerCallback);
            var seq = Interlocked.Increment(ref _chunkSeq);
            try
            {
                SamplesAcquired?.Invoke(this, new DaqSamplesEventArgs
                {
                    TimestampUtc = DateTimeOffset.UtcNow,
                    SampleRateHz = cfg.SampleRateHz,
                    ChannelNames = _channelNames,
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
        catch (DaqException dex)
        {
            if (!ReferenceEquals(currentTask, _task)) return;
            _logger.LogError(dex, "DAQmx read failed; stopping acquisition.");
            lock (_gate)
            {
                if (!ReferenceEquals(currentTask, _task)) return;
                TransitionTo(DaqServiceState.Faulted);
                DisposeTaskNoLock();
            }
            AcquisitionFaulted?.Invoke(this, new DaqFaultEventArgs { Exception = dex, IsFatal = true });
        }
        catch (Exception ex)
        {
            if (!ReferenceEquals(currentTask, _task)) return;
            _logger.LogError(ex, "Unexpected error in DAQ callback.");
            lock (_gate)
            {
                if (!ReferenceEquals(currentTask, _task)) return;
                TransitionTo(DaqServiceState.Faulted);
                DisposeTaskNoLock();
            }
            AcquisitionFaulted?.Invoke(this, new DaqFaultEventArgs { Exception = ex, IsFatal = true });
        }
    }

    // Synchronous dispose used by the StartAsync rollback path (when BuildAndStartTask
    // throws). The Stop path now disposes off the dispatcher via StopAsync's worker.
    private void DisposeTaskNoLock()
    {
        var task = _task;
        _task = null;
        _reader = null;
        _channelNames = Array.Empty<string>();

        if (task is not null)
        {
            try { task.EveryNSamplesRead -= OnEveryNSamplesRead; } catch { }
            try { task.Stop(); } catch { }
            try { task.Dispose(); } catch { }
        }
    }

    private void TransitionTo(DaqServiceState next)
    {
        if (_state == next) return;
        _state = next;
        try { StateChanged?.Invoke(this, next); } catch { /* never let subscriber kill state transitions */ }
    }

    private static AITerminalConfiguration MapTerminal(DaqTerminalConfig t) => t switch
    {
        DaqTerminalConfig.Rse => AITerminalConfiguration.Rse,
        DaqTerminalConfig.Nrse => AITerminalConfiguration.Nrse,
        DaqTerminalConfig.Differential => AITerminalConfiguration.Differential,
        _ => (AITerminalConfiguration)(-1), // NI's "Default"; PseudoDifferential also falls through (USB-6001 doesn't support it).
    };
}

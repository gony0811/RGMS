using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using NationalInstruments.DAQmx;
using NiTask = NationalInstruments.DAQmx.Task;

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

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_state is DaqServiceState.Idle or DaqServiceState.Stopping)
                return Task.CompletedTask;
            TransitionTo(DaqServiceState.Stopping);
            DisposeTaskNoLock();
            TransitionTo(DaqServiceState.Idle);
        }

        _logger.LogInformation("DAQ stopped.");
        return Task.CompletedTask;
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

    public IReadOnlyList<DaqDeviceInfo> EnumerateDevices()
    {
        var devices = DaqSystem.Local.Devices;
        var result = new List<DaqDeviceInfo>(devices.Length);
        foreach (var name in devices)
        {
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
        var reader = _reader;
        var cfg = _config;
        if (reader is null || cfg is null) return;

        try
        {
            var samples = reader.ReadMultiSample(e.NumberOfSamples);
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
            _logger.LogError(dex, "DAQmx read failed; stopping acquisition.");
            lock (_gate) { TransitionTo(DaqServiceState.Faulted); DisposeTaskNoLock(); }
            AcquisitionFaulted?.Invoke(this, new DaqFaultEventArgs { Exception = dex, IsFatal = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in DAQ callback.");
            lock (_gate) { TransitionTo(DaqServiceState.Faulted); DisposeTaskNoLock(); }
            AcquisitionFaulted?.Invoke(this, new DaqFaultEventArgs { Exception = ex, IsFatal = true });
        }
    }

    private void DisposeTaskNoLock()
    {
        var task = _task;
        if (task is not null)
        {
            try { task.EveryNSamplesRead -= OnEveryNSamplesRead; } catch { }
            try { task.Stop(); } catch { }
            try { task.Dispose(); } catch { }
        }
        _task = null;
        _reader = null;
        _channelNames = Array.Empty<string>();
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
        DaqTerminalConfig.PseudoDifferential => AITerminalConfiguration.PseudoDifferential,
        _ => (AITerminalConfiguration)(-1), // NI's "Default"
    };
}

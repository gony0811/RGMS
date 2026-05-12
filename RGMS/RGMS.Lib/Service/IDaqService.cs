namespace RGMS.Lib.Service;

public interface IDaqService : IAsyncDisposable
{
    DaqServiceState State { get; }
    DaqConfiguration? Configuration { get; }

    event EventHandler<DaqSamplesEventArgs>? SamplesAcquired;
    event EventHandler<DaqFaultEventArgs>? AcquisitionFaulted;
    event EventHandler<DaqServiceState>? StateChanged;

    Task StartAsync(DaqConfiguration config, CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
    Task<double[,]> ReadOnceAsync(DaqConfiguration config, int samplesPerChannel, CancellationToken cancellationToken = default);
    IReadOnlyList<DaqDeviceInfo> EnumerateDevices();
}

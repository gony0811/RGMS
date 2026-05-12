using Microsoft.Extensions.DependencyInjection;
using RGMS.Lib.Service.Simulated;

namespace RGMS.Lib.Service.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IDaqService"/>. On Windows the real NI-DAQmx-backed
    /// implementation is registered; otherwise (or when <paramref name="forceSimulator"/>
    /// is true) the simulated implementation is used.
    /// </summary>
    public static IServiceCollection AddDaqService(
        this IServiceCollection services,
        Action<DaqConfiguration>? configure = null,
        bool forceSimulator = false)
    {
        if (!forceSimulator && OperatingSystem.IsWindows())
        {
#if NI_DAQMX
            services.AddSingleton<IDaqService, DaqService>();
#else
            services.AddSingleton<IDaqService, SimulatedDaqService>();
#endif
        }
        else
        {
            services.AddSingleton<IDaqService, SimulatedDaqService>();
        }

        if (configure is not null)
            services.Configure(configure);

        return services;
    }
}

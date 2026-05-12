using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace RGMS.Lib.Data.Extensions;

public static class DataServiceCollectionExtensions
{
    /// <summary>
    /// Registers the RGMS EF Core SQLite context and the singleton settings store.
    /// </summary>
    public static IServiceCollection AddRgmsData(this IServiceCollection services, string connectionString)
    {
        services.AddDbContextFactory<RgmsDbContext>(options => options.UseSqlite(connectionString));
        services.AddSingleton<ISettingsStore, SettingsStore>();
        return services;
    }
}

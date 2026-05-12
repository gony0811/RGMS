using System.Runtime.Loader;
using Microsoft.EntityFrameworkCore;
using RGMS.Components;
using RGMS.Lib.Data;
using RGMS.Lib.Data.Extensions;
using RGMS.Lib.Service.Extensions;

// NI-DAQmx (NationalInstruments.DAQmx / Common) are .NET Framework 4.x assemblies
// copied to our output via RGMS.Lib's <Private>true</Private> reference. SDK-style
// ProjectReference does not propagate the library's file references into the
// consuming app's .deps.json, so .NET 8's host fails to seed TPA with them and
// the JIT throws FileNotFoundException on first DaqSystem call. Probe app-base
// directly to bridge that gap.
if (OperatingSystem.IsWindows())
{
    AssemblyLoadContext.Default.Resolving += static (ctx, name) =>
    {
        if (name.Name is not ("NationalInstruments.DAQmx" or "NationalInstruments.Common"))
            return null;
        var path = Path.Combine(AppContext.BaseDirectory, name.Name + ".dll");
        if (!File.Exists(path)) return null;
        Console.WriteLine($"[NI resolver] loading {name.Name} from {path}");
        return ctx.LoadFromAssemblyPath(path);
    };
}

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// NI USB-6001 DAQ — real device on Windows, simulator elsewhere.
builder.Services.AddDaqService();

// SQLite-backed settings store (EF Core).
var connectionString = builder.Configuration.GetConnectionString("RgmsDb")
                       ?? "Data Source=rgms.db";
builder.Services.AddRgmsData(connectionString);

var app = builder.Build();

// Apply pending EF Core migrations and seed defaults on startup.
using (var scope = app.Services.CreateScope())
{
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<RgmsDbContext>>();
    await using var db = await factory.CreateDbContextAsync();
    await db.Database.MigrateAsync();

    var store = scope.ServiceProvider.GetRequiredService<ISettingsStore>();
    await store.LoadAsync();
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

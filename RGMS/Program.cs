using Microsoft.EntityFrameworkCore;
using RGMS.Components;
using RGMS.Lib.Data;
using RGMS.Lib.Data.Extensions;
using RGMS.Lib.Service.Extensions;

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

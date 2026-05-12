using RGMS.Components;
using RGMS.Lib.Service;
using RGMS.Lib.Service.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// NI USB-6001 DAQ — real device on Windows, simulator elsewhere.
builder.Services.AddDaqService(cfg =>
{
    var defaults = DaqConfiguration.DefaultRgms();
    cfg.DeviceName = defaults.DeviceName;
    cfg.SampleRateHz = defaults.SampleRateHz;
    cfg.SamplesPerChannelPerCallback = defaults.SamplesPerChannelPerCallback;
    cfg.Channels = defaults.Channels;
});

var app = builder.Build();

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
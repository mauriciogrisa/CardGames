using Microsoft.AspNetCore.Components.Server;
using Microsoft.AspNetCore.ResponseCompression;
using CardGames.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddHubOptions(o =>
    {
        o.ClientTimeoutInterval       = TimeSpan.FromSeconds(60);
        o.HandshakeTimeout            = TimeSpan.FromSeconds(15);
        o.KeepAliveInterval           = TimeSpan.FromSeconds(20);
    });

builder.Services.Configure<CircuitOptions>(o =>
{
    o.DisconnectedCircuitRetentionPeriod = TimeSpan.FromMinutes(3);
});

builder.Services.AddSingleton<GameLogger>();
builder.Services.AddScoped<LanguageService>();
builder.Services.AddScoped<AiDecisionService>();
builder.Services.AddScoped<WebGameService>();

builder.Services.AddHealthChecks();

builder.Services.AddResponseCompression(o =>
{
    o.EnableForHttps = true;
    o.Providers.Add<BrotliCompressionProvider>();
    o.Providers.Add<GzipCompressionProvider>();
});

var app = builder.Build();

app.UseResponseCompression();
app.UseStaticFiles();
app.UseAntiforgery();

app.MapHealthChecks("/health");

app.MapRazorComponents<CardGames.App>()
    .AddInteractiveServerRenderMode();

// Auto-open browser only in local development — not on Azure (headless server).
if (app.Environment.IsDevelopment())
{
    app.Lifetime.ApplicationStarted.Register(() =>
    {
        var url = app.Urls.FirstOrDefault() ?? "http://localhost:5000";
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url)
        {
            UseShellExecute = true
        });
    });
}

app.Run();

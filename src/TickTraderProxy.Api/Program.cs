using Microsoft.Extensions.Options;
using TickTraderProxy.Api.Endpoints;
using TickTraderProxy.Api.Hmac;
using TickTraderProxy.Api.Hubs;
using TickTraderProxy.Api.Models;
using TickTraderProxy.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// --- Конфиг ---
builder.Services.AddOptions<TickTraderOptions>()
    .Bind(builder.Configuration.GetSection(TickTraderOptions.SectionName))
    .Validate(o => !string.IsNullOrWhiteSpace(o.BaseUrl), "TickTrader:BaseUrl обязателен")
    .ValidateOnStart();

// --- Инфраструктура ---
builder.Services.AddHttpContextAccessor();
builder.Services.AddDataProtection();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.Cookie.Name = ".TickTraderProxy.Session";
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.IdleTimeout = TimeSpan.FromHours(1);
});
builder.Services.AddSignalR();

builder.Services.AddSingleton<SessionCredentialsStore>();
builder.Services.AddSingleton<MarketFeedCredentialStore>();
builder.Services.AddHostedService<QuoteBroadcastService>();

// --- Типизированный клиент TickTrader + HMAC-подпись ---
builder.Services.AddTransient<HmacAuthHandler>();
builder.Services.AddHttpClient<TickTraderClient>()
    .AddHttpMessageHandler<HmacAuthHandler>()
    .ConfigurePrimaryHttpMessageHandler(sp =>
    {
        var options = sp.GetRequiredService<IOptions<TickTraderOptions>>().Value;
        var handler = new HttpClientHandler();
        // ВРЕМЕННОЕ послабление TLS только для demo :8443 (самоподписанный серт). См. README.
        if (options.AllowInvalidServerCertificate)
            handler.ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        return handler;
    });

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseSession();

app.MapSessionEndpoints();
app.MapProxyEndpoints();
app.MapHub<MarketDataHub>("/hubs/marketdata");

// SPA-фолбэк: всё остальное отдаём из index.html (фронт собирается в wwwroot).
app.MapFallbackToFile("index.html");

app.Run();

// Открыто для интеграционных тестов (WebApplicationFactory).
public partial class Program;

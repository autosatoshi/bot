using AutoBot.Models;
using AutoBot.Services;

var builder = Host.CreateApplicationBuilder(args);

// Configure appsettings files manually to ensure environment-specific settings are loaded
var contentRoot = builder.Environment.ContentRootPath;
builder.Configuration
    .AddJsonFile(Path.Combine(contentRoot, "appsettings.json"), optional: true, reloadOnChange: true)
    .AddJsonFile(Path.Combine(contentRoot, $"appsettings.Development.json"), optional: true, reloadOnChange: true);

// Configure Options pattern
builder.Services.AddOptions<LnMarketsOptions>()
    .Bind(builder.Configuration.GetSection(LnMarketsOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// Add HttpClient and logging
builder.Services.AddHttpClient();
builder.Services.AddLogging();

builder.Services.AddSingleton<ILnMarketsApiService, LnMarketsApiService>();
builder.Services.AddSingleton<ITradeManager, TradeManager>();
builder.Services.AddHostedService<LnMarketsBackgroundService>();

var host = builder.Build();
await host.RunAsync();

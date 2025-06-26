using AutoBot.Models;
using AutoBot.Services;

var builder = WebApplication.CreateBuilder(args);

// Configure Options pattern
builder.Services.Configure<LnMarketsOptions>(
    builder.Configuration.GetSection(LnMarketsOptions.SectionName));

builder.Services.AddOptions<LnMarketsOptions>()
    .Bind(builder.Configuration.GetSection(LnMarketsOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddHttpClient<ILnMarketsApiService, LnMarketsApiService>();
builder.Services.AddHostedService<LnMarketsBackgroundService>();

builder.Services.AddControllers();
builder.Services.AddHttpContextAccessor();

var app = builder.Build();

app.UseDefaultFiles()
    .UseStaticFiles()
    .UseAuthentication()
    .UseAuthorization()
    .UseHttpsRedirection();

app.MapControllers();
app.Run();
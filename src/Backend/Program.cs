using AutoBot.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<ILnMarketsApiService, LnMarketsApiService>();
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
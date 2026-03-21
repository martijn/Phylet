using Phylet.Data;
using Phylet.Data.Configuration;
using Phylet.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddOptions<DlnaOptions>()
    .Bind(builder.Configuration.GetSection("Dlna"))
    .Validate(options => options.DefaultSubscriptionTimeoutSeconds is >= 60 and <= 86400,
        "Dlna:DefaultSubscriptionTimeoutSeconds must be between 60 and 86400.")
    .ValidateOnStart();

builder.Services.AddPhyletData(builder.Configuration);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ServerAddressResolver>();
builder.Services.AddSingleton<DidlBuilder>();
builder.Services.AddSingleton<EventSubscriptionService>();
builder.Services.AddSingleton<LibraryScanService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<LibraryScanService>());
builder.Services.AddHostedService<StartupDiagnosticsService>();
builder.Services.AddHostedService<SsdpService>();
builder.Services.AddControllers();

var app = builder.Build();

app.Use(async (context, next) =>
{
    app.Logger.LogDebug("HTTP request {Method} {Path}{QueryString}", context.Request.Method, context.Request.Path, context.Request.QueryString);
    await next();
    app.Logger.LogDebug("HTTP response {StatusCode} {Method} {Path}{QueryString}", context.Response.StatusCode, context.Request.Method, context.Request.Path, context.Request.QueryString);
});

app.MapControllers();

await app.Services.InitializePhyletAsync();

app.Run();

public partial class Program;

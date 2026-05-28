using ForexBot.Api;
using ForexBot.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddSingleton<AccountRegistry>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<AccountRegistry>());

var port = Environment.GetEnvironmentVariable("API_PORT") ?? "5050";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.Hosting.Lifetime", LogLevel.Information);

var app = builder.Build();

app.UseMiddleware<ApiKeyMiddleware>();
app.MapControllers();

var adminKey = Environment.GetEnvironmentVariable("ADMIN_KEY");
Console.WriteLine("=== XAUUSD Gold Scalping Bot — Multi-Account API ===");
Console.WriteLine($"Port      : {port}");
Console.WriteLine($"Admin key : {(string.IsNullOrWhiteSpace(adminKey) ? "NOT SET — /api/accounts is open (dev mode)" : "set via ADMIN_KEY")}");
Console.WriteLine();
Console.WriteLine("── Account Management (admin) ──────────────────");
Console.WriteLine($"  GET    /api/accounts                   list all accounts");
Console.WriteLine($"  POST   /api/accounts                   register new account");
Console.WriteLine($"  DELETE /api/accounts/{{apiKey}}           remove account");
Console.WriteLine();
Console.WriteLine("── Per-Account Endpoints (use X-Api-Key header) ─");
Console.WriteLine($"  GET    /api/status");
Console.WriteLine($"  GET    /api/status/positions");
Console.WriteLine($"  GET    /api/status/trades");
Console.WriteLine($"  POST   /api/bot/start | /api/bot/stop");
Console.WriteLine($"  POST   /api/trade/buy | /api/trade/sell | /api/trade/close");
Console.WriteLine($"  GET    /api/settings  |  PUT /api/settings");
Console.WriteLine("═════════════════════════════════════════════════\n");

await app.RunAsync();

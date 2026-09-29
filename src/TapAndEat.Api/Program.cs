using Microsoft.AspNetCore.Identity;
using TapAndEat.Api.Auth;
using TapAndEat.Api.Data;
using TapAndEat.Api.Hubs;
using TapAndEat.Api.Infrastructure;
using TapAndEat.Api.Jobs;
using TapAndEat.Api.Models;
using TapAndEat.Api.Payments;
using TapAndEat.Api.Repositories;
using TapAndEat.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// ---- Services ----

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSignalR();

builder.Services.AddCors(options =>
{
    // Sprint 1 UI is served from the same origin (wwwroot), but CORS is left
    // open for local development against a separate frontend dev server.
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyHeader().AllowAnyMethod().SetIsOriginAllowed(_ => true).AllowCredentials());
});

// Repositories — in-memory, registered as singletons so data survives for
// the lifetime of the process. Swap these for EF Core-backed
// implementations later; nothing else needs to change.
builder.Services.AddSingleton<IUserRepository, InMemoryUserRepository>();
builder.Services.AddSingleton<IMenuRepository, InMemoryMenuRepository>();
builder.Services.AddSingleton<IPasswordResetRepository, InMemoryPasswordResetRepository>();
builder.Services.AddSingleton<IOrderRepository, InMemoryOrderRepository>();
builder.Services.AddSingleton<IPaymentRepository, InMemoryPaymentRepository>();
builder.Services.AddSingleton<IQueueTokenRepository, InMemoryQueueTokenRepository>();
builder.Services.AddSingleton<IRfidTapRepository, InMemoryRfidTapRepository>();
builder.Services.AddSingleton<IWalletRepository, InMemoryWalletRepository>();

// Infrastructure + options
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IEventBroadcaster, EventBroadcaster>();
builder.Services.Configure<PaymentOptions>(builder.Configuration.GetSection("Payments"));
builder.Services.Configure<OrderOptions>(builder.Configuration.GetSection("Orders"));
builder.Services.Configure<KitchenOptions>(builder.Configuration.GetSection("Kitchen"));
builder.Services.Configure<BusinessOptions>(builder.Configuration.GetSection("Business"));
builder.Services.Configure<JobsOptions>(builder.Configuration.GetSection("Jobs"));

// Payments
builder.Services.AddSingleton<MockGatewayLedger>();
builder.Services.AddSingleton<BkashTokenCache>();
builder.Services.AddHttpClient<BkashGateway>();
builder.Services.AddHttpClient<NagadGateway>();
builder.Services.AddSingleton<IPaymentGatewayResolver, PaymentGatewayResolver>();
builder.Services.AddSingleton<IPaymentTokenService, PaymentTokenService>();
builder.Services.AddHostedService<ScheduledJobsService>();

// Auth
builder.Services.AddSingleton<ITokenService, TokenService>();
builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services
    .AddAuthentication(AuthSchemes.Bearer)
    .AddScheme<BearerTokenAuthenticationOptions, BearerTokenAuthenticationHandler>(AuthSchemes.Bearer, null);
builder.Services.AddAuthorization();

// Application services
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IMenuService, MenuService>();
builder.Services.AddScoped<IAdminService, AdminService>();
builder.Services.AddScoped<IWalletService, WalletService>();
builder.Services.AddScoped<ISubsidyService, SubsidyService>();
builder.Services.AddScoped<IOrderService, OrderService>();
builder.Services.AddScoped<IQueueService, QueueService>();
builder.Services.AddScoped<IPaymentService, PaymentService>();
builder.Services.AddScoped<ICounterService, CounterService>();

var app = builder.Build();

// ---- Middleware pipeline ----

app.UseDefaultFiles();   // serves wwwroot/index.html at "/"
app.UseStaticFiles();    // serves the login/menu/admin HTML pages and JS/CSS

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<MenuHub>("/hubs/menu");

// Seed a default admin account + sample menu so the API is usable immediately.
using (var scope = app.Services.CreateScope())
{
    await DbSeeder.SeedAsync(scope.ServiceProvider);
}

app.Run();

// Exposed for WebApplicationFactory-style integration tests.
public partial class Program { }
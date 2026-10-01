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

namespace TapAndEat.Api.Startup;

/// <summary>
/// All service registration and the middleware pipeline, moved out of
/// Program.cs in Sprint 2 so the exact same app can be started in-process by
/// the end-to-end tests (tests/.../Suites/EndToEndTests.cs) with different
/// command-line configuration. Program.cs is now just Build → Seed → Run.
/// </summary>
public static class AppHost
{
    public static WebApplication Build(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        var config = builder.Configuration;

        // ---- Options ----
        builder.Services.Configure<PaymentOptions>(config.GetSection("Payments"));
        builder.Services.Configure<KitchenOptions>(config.GetSection("Kitchen"));
        builder.Services.Configure<OrderOptions>(config.GetSection("Orders"));
        builder.Services.Configure<BusinessOptions>(config.GetSection("Business"));
        builder.Services.Configure<JobsOptions>(config.GetSection("Jobs"));

        // ---- Framework ----
        // AddApplicationPart: controllers are found even when the app is booted from
        // another assembly (the end-to-end tests do exactly that).
        builder.Services.AddControllers().AddApplicationPart(typeof(AppHost).Assembly);
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSignalR();
        builder.Services.AddSingleton(TimeProvider.System);

        builder.Services.AddCors(options =>
        {
            // The UI is served from the same origin (wwwroot); CORS stays open
            // for local development against a separate frontend dev server.
            options.AddDefaultPolicy(policy =>
                policy.AllowAnyHeader().AllowAnyMethod().SetIsOriginAllowed(_ => true).AllowCredentials());
        });

        // ---- Repositories (in-memory, singletons). Swap for EF Core-backed
        // implementations to persist — see README "Persistence". ----
        builder.Services.AddSingleton<IUserRepository, InMemoryUserRepository>();
        builder.Services.AddSingleton<IMenuRepository, InMemoryMenuRepository>();
        builder.Services.AddSingleton<IPasswordResetRepository, InMemoryPasswordResetRepository>();
        builder.Services.AddSingleton<IOrderRepository, InMemoryOrderRepository>();
        builder.Services.AddSingleton<IPaymentRepository, InMemoryPaymentRepository>();
        builder.Services.AddSingleton<IWalletRepository, InMemoryWalletRepository>();
        builder.Services.AddSingleton<IQueueTokenRepository, InMemoryQueueTokenRepository>();
        builder.Services.AddSingleton<IRfidTapRepository, InMemoryRfidTapRepository>();

        // ---- Auth ----
        builder.Services.AddSingleton<ITokenService, TokenService>();
        builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
        builder.Services
            .AddAuthentication(AuthSchemes.Bearer)
            .AddScheme<BearerTokenAuthenticationOptions, BearerTokenAuthenticationHandler>(AuthSchemes.Bearer, null);
        builder.Services.AddAuthorization();

        // ---- Sprint 1 application services ----
        builder.Services.AddScoped<IAuthService, AuthService>();
        builder.Services.AddScoped<IMenuService, MenuService>();
        builder.Services.AddScoped<IAdminService, AdminService>();

        // ---- Sprint 2: payments ----
        builder.Services.AddSingleton<MockGatewayLedger>();
        builder.Services.AddSingleton<BkashTokenCache>();
        builder.Services.AddHttpClient<BkashGateway>((sp, http) =>
            http.Timeout = TimeSpan.FromSeconds(Math.Max(1, config.GetValue("Payments:Bkash:TimeoutSeconds", 20))));
        builder.Services.AddHttpClient<NagadGateway>((sp, http) =>
            http.Timeout = TimeSpan.FromSeconds(Math.Max(1, config.GetValue("Payments:Nagad:TimeoutSeconds", 20))));
        builder.Services.AddScoped<IPaymentGatewayResolver, PaymentGatewayResolver>();
        builder.Services.AddSingleton<IPaymentTokenService, PaymentTokenService>();
        builder.Services.AddScoped<IPaymentService, PaymentService>();

        // ---- Sprint 2: orders, wallet, queue, counter ----
        builder.Services.AddSingleton<IEventBroadcaster, EventBroadcaster>();
        builder.Services.AddScoped<IOrderService, OrderService>();
        builder.Services.AddScoped<IQueueService, QueueService>();
        builder.Services.AddScoped<IWalletService, WalletService>();
        builder.Services.AddScoped<ISubsidyService, SubsidyService>();
        builder.Services.AddScoped<ICounterService, CounterService>();
        builder.Services.AddHostedService<ScheduledJobsService>();

        var app = builder.Build();

        // ---- Middleware pipeline ----
        app.UseDefaultFiles();   // serves wwwroot/index.html at "/"
        app.UseStaticFiles();

        app.UseCors();
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapControllers();
        app.MapHub<MenuHub>("/hubs/menu");

        return app;
    }

    /// <summary>Seeds demo accounts, wallet and menu so a fresh run is immediately usable.</summary>
    public static async Task SeedAsync(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        await DbSeeder.SeedAsync(scope.ServiceProvider);
    }
}

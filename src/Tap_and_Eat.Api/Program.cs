using Microsoft.AspNetCore.Identity;
using TapAndEat.Api.Auth;
using TapAndEat.Api.Data;
using TapAndEat.Api.Hubs;
using TapAndEat.Api.Models;
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
// the lifetime of the process. Swap these three lines for EF Core-backed
// implementations in Sprint 2; nothing else needs to change.
builder.Services.AddSingleton<IUserRepository, InMemoryUserRepository>();
builder.Services.AddSingleton<IMenuRepository, InMemoryMenuRepository>();
builder.Services.AddSingleton<IPasswordResetRepository, InMemoryPasswordResetRepository>();

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

using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using TapAndEat.Api.Hubs;
using TapAndEat.Api.Models;
using TapAndEat.Api.Repositories;
using TapAndEat.Api.Services;

namespace TapAndEat.Tests.Framework;

/// <summary>
/// Each test gets brand-new repositories/services so tests never leak state
/// into one another, the same isolation xUnit gives you by instantiating the
/// test class fresh per [Fact].
/// </summary>
public class ServiceFixture
{
    public IUserRepository Users { get; } = new InMemoryUserRepository();
    public IMenuRepository Menu { get; } = new InMemoryMenuRepository();
    public IPasswordResetRepository ResetTokens { get; } = new InMemoryPasswordResetRepository();
    public ITokenService TokenService { get; } = new TokenService();
    public IPasswordHasher<User> PasswordHasher { get; } = new PasswordHasher<User>();

    public IAuthService BuildAuthService() =>
        new AuthService(Users, ResetTokens, TokenService, PasswordHasher, NullLogger<AuthService>.Instance);

    public IMenuService BuildMenuService() =>
        new MenuService(Menu, new NullHubContext());

    public IAdminService BuildAdminService() =>
        new AdminService(Users, Menu, TokenService, PasswordHasher);

    /// <summary>
    /// A no-op IHubContext so MenuService's SignalR broadcast calls succeed
    /// with no real clients connected — the broadcast call itself (not
    /// delivery to a browser) is what these unit tests exercise.
    /// </summary>
    private class NullHubContext : IHubContext<MenuHub>
    {
        public IHubClients Clients { get; } = new NullHubClients();
        public IGroupManager Groups { get; } = new NullGroupManager();

        private class NullHubClients : IHubClients
        {
            private readonly IClientProxy _proxy = new NullClientProxy();
            public IClientProxy All => _proxy;
            public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => _proxy;
            public IClientProxy Client(string connectionId) => _proxy;
            public IClientProxy Clients(IReadOnlyList<string> connectionIds) => _proxy;
            public IClientProxy Group(string groupName) => _proxy;
            public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => _proxy;
            public IClientProxy Groups(IReadOnlyList<string> groupNames) => _proxy;
            public IClientProxy User(string userId) => _proxy;
            public IClientProxy Users(IReadOnlyList<string> userIds) => _proxy;
        }

        private class NullClientProxy : IClientProxy
        {
            public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default) =>
                Task.CompletedTask;
        }

        private class NullGroupManager : IGroupManager
        {
            public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) =>
                Task.CompletedTask;
            public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) =>
                Task.CompletedTask;
        }
    }
}

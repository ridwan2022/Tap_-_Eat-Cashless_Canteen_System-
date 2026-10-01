using TapAndEat.Api.Startup;

// Service registration and the middleware pipeline live in Startup/AppHost.cs
// (so the end-to-end tests can boot the very same app in-process).
var app = AppHost.Build(args);
await AppHost.SeedAsync(app);
app.Run();

// Exposed for WebApplicationFactory-style integration tests.
public partial class Program { }

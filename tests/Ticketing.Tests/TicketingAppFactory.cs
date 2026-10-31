using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Ticketing.Tests;

/// <summary>
/// Runs the real application — the real composition root, migrations, and seeding — against a
/// specific store file, so a test can dispose it and build another one over the same file.
/// </summary>
public sealed class TicketingAppFactory(string storePath) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        // A host setting, not a configuration source added later: the application reads its connection
        // string while it is being built, before late configuration sources would be visible.
        builder.UseSetting("ConnectionStrings:Ticketing", $"Data Source={storePath};Pooling=False");
    }
}
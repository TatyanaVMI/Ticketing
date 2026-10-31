using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Ticketing.Data;
using Ticketing.Services;

namespace Ticketing.Tests;

/// <summary>
/// A temporary SQLite store on disk — on disk, not in memory, because only a file can survive a
/// restart (research R10).
/// </summary>
public sealed class TestStore : IAsyncDisposable
{
    private readonly string _path;

    public TestStore()
    {
        _path = Path.Combine(Path.GetTempPath(), $"ticketing-{Guid.NewGuid():N}.db");

        Factory = new TestContextFactory(new DbContextOptionsBuilder<TicketingDbContext>()
            .UseSqlite($"Data Source={_path};Pooling=False")
            .Options);
    }

    public IDbContextFactory<TicketingDbContext> Factory { get; }

    public string StorePath => _path;

    /// <summary>What the application does on start: create the schema, then seed when the store is empty.</summary>
    public async Task SeedAsync()
    {
        await using (var db = await Factory.CreateDbContextAsync())
        {
            await db.Database.MigrateAsync();
        }

        await TicketingSeeder.EnsureSeededAsync(Factory);
    }

    /// <summary>A service that has already read the venue, as a circuit would.</summary>
    public async Task<TicketingService> CreateServiceAsync()
    {
        var service = new TicketingService(Factory, NullLogger<TicketingService>.Instance);
        await service.LoadAsync();
        return service;
    }

    public async ValueTask DisposeAsync()
    {
        await Task.CompletedTask;

        // Pooled connections would keep the file locked on Windows.
        SqliteConnection.ClearAllPools();

        foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
        {
            var file = _path + suffix;
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
    }

    private sealed class TestContextFactory(DbContextOptions<TicketingDbContext> options)
        : IDbContextFactory<TicketingDbContext>
    {
        public TicketingDbContext CreateDbContext() => new(options);
    }
}
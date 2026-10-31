using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Ticketing.Data;

/// <summary>
/// Lets <c>dotnet ef</c> build a context although the application registers a factory, not a context
/// (research R3). Used for migrations only; it never opens the store the application runs against.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<TicketingDbContext>
{
    public TicketingDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<TicketingDbContext>()
            .UseSqlite("Data Source=App_Data/ticketing.db")
            .Options;

        return new TicketingDbContext(options);
    }
}
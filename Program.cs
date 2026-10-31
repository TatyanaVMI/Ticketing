using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Ticketing;
using Ticketing.Data;
using Ticketing.Services;

var builder = WebApplication.CreateBuilder(args);

var connectionString = ResolveStoreConnectionString(builder);

builder.Services.AddDbContextFactory<TicketingDbContext>(options => options.UseSqlite(connectionString));
builder.Services.AddScoped<TicketingService>();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();

var app = builder.Build();

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

await PrepareStoreAsync(app);

app.Run();

/// <summary>
/// Creates the schema when the store is missing and seeds the venue when it is empty. A store that
/// cannot be read at all is reported by the page instead of stopping the process (spec FR-015).
/// </summary>
static async Task PrepareStoreAsync(WebApplication app)
{
    try
    {
        await using var scope = app.Services.CreateAsyncScope();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TicketingDbContext>>();

        await using (var db = await contextFactory.CreateDbContextAsync())
        {
            await db.Database.MigrateAsync();
        }

        await TicketingSeeder.EnsureSeededAsync(contextFactory);
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
        app.Logger.LogError(
            ex,
            "The ticketing store could not be prepared. The venue stays unavailable until it can be read.");
    }
}

/// <summary>
/// Runs <c>Data Source=App_Data/ticketing.db</c> against the content root, so the same file is used
/// whatever the working directory is (research R8).
/// </summary>
static string ResolveStoreConnectionString(WebApplicationBuilder builder)
{
    var configured = builder.Configuration.GetConnectionString("Ticketing")
        ?? throw new InvalidOperationException("Connection string 'Ticketing' is not configured.");

    var store = new SqliteConnectionStringBuilder(configured);

    if (!Path.IsPathRooted(store.DataSource))
    {
        store.DataSource = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, store.DataSource));
    }

    var directory = Path.GetDirectoryName(store.DataSource);
    if (!string.IsNullOrEmpty(directory))
    {
        Directory.CreateDirectory(directory);
    }

    return store.ToString();
}

/// <summary>Exposed so the host-level tests can build the real application (research R10).</summary>
public partial class Program { }
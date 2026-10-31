using Microsoft.EntityFrameworkCore;

namespace Ticketing.Data;

/// <summary>
/// The store: one event, its seat inventory, and every completed purchase. Created through an
/// <see cref="IDbContextFactory{TContext}"/> so no context outlives a single operation (research R2).
/// </summary>
public class TicketingDbContext(DbContextOptions<TicketingDbContext> options) : DbContext(options)
{
    public DbSet<EventRecord> Events => Set<EventRecord>();

    public DbSet<SeatRecord> Seats => Set<SeatRecord>();

    public DbSet<PurchaseRecord> Purchases => Set<PurchaseRecord>();

    public DbSet<PurchaseLineRecord> PurchaseLines => Set<PurchaseLineRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EventRecord>(entity =>
        {
            entity.ToTable("Events");
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Artist).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Venue).IsRequired().HasMaxLength(200);
        });

        modelBuilder.Entity<SeatRecord>(entity =>
        {
            entity.ToTable("Seats");

            // One seat per position per event: duplicates cannot be stored (spec FR-002, SC-007).
            entity.HasIndex(s => new { s.EventId, s.Row, s.Number }).IsUnique();

            entity.Property(s => s.Row).IsRequired().HasMaxLength(1);
            entity.Property(s => s.Price).HasPrecision(10, 2);

            // Stored as text, not as an integer: the enum's membership has already changed once.
            entity.Property(s => s.Status).IsRequired().HasConversion<string>().HasMaxLength(20);

            entity.HasOne(s => s.Event)
                .WithMany()
                .HasForeignKey(s => s.EventId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PurchaseRecord>(entity =>
        {
            entity.ToTable("Purchases");
            entity.Property(p => p.TotalAmount).HasPrecision(10, 2);

            entity.HasOne<EventRecord>()
                .WithMany()
                .HasForeignKey(p => p.EventId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PurchaseLineRecord>(entity =>
        {
            entity.ToTable("PurchaseLines");

            // The same seat cannot appear twice in one purchase.
            entity.HasIndex(l => new { l.PurchaseId, l.SeatId }).IsUnique();

            entity.Property(l => l.PricePaid).HasPrecision(10, 2);

            entity.HasOne(l => l.Purchase)
                .WithMany(p => p.Lines)
                .HasForeignKey(l => l.PurchaseId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne<SeatRecord>()
                .WithMany()
                .HasForeignKey(l => l.SeatId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
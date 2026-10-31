using Microsoft.EntityFrameworkCore;

namespace Ticketing.Data;

/// <summary>
/// Builds the fresh venue described by
/// <c>specs/001-persist-ticketing-state/contracts/venue-seed-contract.md</c>.
/// The inventory is a pinned constant: no randomness, no clock, no machine-dependent value (research R4).
/// </summary>
public static class TicketingSeeder
{
    public const string EventName = "Midnight Frequencies Tour";

    public const string EventArtist = "The Static Fields";

    public const string EventVenue = "Grand Hall Auditorium";

    public const int RowCount = 10;

    public const int SeatsPerRow = 12;

    /// <summary>
    /// The performance starts at 20:00 on 2026-11-21. The offset is fixed so the seed never depends on
    /// the time zone of the machine it runs on.
    /// </summary>
    public static readonly DateTimeOffset StartsAt = new(2026, 11, 21, 20, 0, 0, TimeSpan.FromHours(1));

    private const string Rows = "ABCDEFGHIJ";

    /// <summary>The 28 seats the venue already marks as taken — exactly the set pinned by the contract.</summary>
    private static readonly HashSet<string> TakenSeats = new(StringComparer.Ordinal)
    {
        "A3", "A9",
        "B7", "B12",
        "C6", "C7",
        "D6", "D10", "D12",
        "E1", "E4", "E6", "E12",
        "F10",
        "G2", "G6", "G8",
        "H3", "H6", "H9",
        "I4", "I6", "I8", "I10",
        "J3", "J6", "J8", "J9"
    };

    /// <summary>
    /// Seeds the venue only when the store has no event. A store that already has content is left
    /// exactly as it is: no new seats, no status resets, no price changes (spec FR-011, SC-007).
    /// </summary>
    public static async Task EnsureSeededAsync(
        IDbContextFactory<TicketingDbContext> contextFactory,
        CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        if (await db.Events.AnyAsync(cancellationToken))
        {
            return;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // The guard is repeated inside the transaction: an existing event means "do nothing at all".
        if (await db.Events.AnyAsync(cancellationToken))
        {
            return;
        }

        var eventRecord = new EventRecord
        {
            Name = EventName,
            Artist = EventArtist,
            StartsAt = StartsAt,
            Venue = EventVenue
        };

        db.Events.Add(eventRecord);

        for (var rowIndex = 0; rowIndex < RowCount; rowIndex++)
        {
            var row = Rows[rowIndex].ToString();
            var price = PriceForRow(rowIndex);

            for (var number = 1; number <= SeatsPerRow; number++)
            {
                db.Seats.Add(new SeatRecord
                {
                    Event = eventRecord,
                    Row = row,
                    Number = number,
                    Price = price,
                    Status = TakenSeats.Contains($"{row}{number}") ? SeatStatus.Taken : SeatStatus.Available
                });
            }
        }

        // One transaction: an interrupted start cannot leave a half-built venue.
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>Row <c>A</c> costs 150, <c>B</c>–<c>D</c> 120, <c>E</c>–<c>H</c> 90, <c>I</c>–<c>J</c> 60.</summary>
    public static decimal PriceForRow(int rowIndex) => rowIndex switch
    {
        0 => 150m,
        >= 1 and <= 3 => 120m,
        >= 4 and <= 7 => 90m,
        _ => 60m
    };
}
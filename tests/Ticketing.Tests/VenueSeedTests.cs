using Microsoft.EntityFrameworkCore;
using Ticketing.Data;
using Ticketing.Models;

namespace Ticketing.Tests;

/// <summary>
/// SC-006 / SC-007: a fresh store yields the venue pinned by the seed contract, identically every
/// time, and a populated store is never re-seeded.
/// </summary>
public sealed class VenueSeedTests
{
    private static readonly string[] ExpectedTakenSeats =
    [
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
    ];

    private static readonly Dictionary<string, decimal> ExpectedPrices = new()
    {
        ["A"] = 150m,
        ["B"] = 120m,
        ["C"] = 120m,
        ["D"] = 120m,
        ["E"] = 90m,
        ["F"] = 90m,
        ["G"] = 90m,
        ["H"] = 90m,
        ["I"] = 60m,
        ["J"] = 60m
    };

    [Fact]
    public async Task Fresh_store_contains_the_whole_venue_and_exactly_the_pinned_taken_seats()
    {
        await using var store = new TestStore();
        await store.SeedAsync();

        await using var db = await store.Factory.CreateDbContextAsync();
        var eventRecord = Assert.Single(await db.Events.AsNoTracking().ToListAsync());

        Assert.Equal("Midnight Frequencies Tour", eventRecord.Name);
        Assert.Equal("The Static Fields", eventRecord.Artist);
        Assert.Equal("Grand Hall Auditorium", eventRecord.Venue);
        Assert.Equal(TicketingSeeder.StartsAt, eventRecord.StartsAt);

        var seats = await db.Seats
            .AsNoTracking()
            .OrderBy(seat => seat.Row)
            .ThenBy(seat => seat.Number)
            .ToListAsync();

        Assert.Equal(120, seats.Count);
        Assert.Equal("ABCDEFGHIJ", string.Concat(seats.Select(seat => seat.Row).Distinct()));

        foreach (var row in seats.GroupBy(seat => seat.Row))
        {
            Assert.Equal(Enumerable.Range(1, 12), row.Select(seat => seat.Number).OrderBy(number => number));
            Assert.All(row, seat => Assert.Equal(ExpectedPrices[row.Key], seat.Price));
        }

        var taken = seats
            .Where(seat => seat.Status == SeatStatus.Taken)
            .Select(seat => $"{seat.Row}{seat.Number}")
            .OrderBy(label => label, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(ExpectedTakenSeats.OrderBy(label => label, StringComparer.Ordinal), taken);
        Assert.All(
            seats.Where(seat => seat.Status != SeatStatus.Taken),
            seat => Assert.Equal(SeatStatus.Available, seat.Status));
    }

    [Fact]
    public async Task Two_fresh_stores_produce_the_identical_venue()
    {
        await using var first = new TestStore();
        await using var second = new TestStore();

        await first.SeedAsync();
        await second.SeedAsync();

        Assert.Equal(await InventoryAsync(first), await InventoryAsync(second));
    }

    [Fact]
    public async Task Starting_again_over_a_populated_store_changes_nothing()
    {
        await using var store = new TestStore();
        await store.SeedAsync();

        // A visitor buys a seat in between, as a running application would.
        var service = await store.CreateServiceAsync();
        var seat = service.Rows.SelectMany(row => row).First(candidate => candidate.State == SeatState.Available);
        await service.ToggleSeat(seat);
        var outcome = await service.PurchaseAsync();
        Assert.Equal(PurchaseOutcomeKind.Succeeded, outcome.Kind);

        var purchaseId = outcome.PurchaseId!.Value;

        var before = await InventoryAsync(store);
        var purchaseBefore = await PurchaseSnapshotAsync(store, purchaseId);

        await store.SeedAsync();
        await store.SeedAsync();

        Assert.Equal(before, await InventoryAsync(store));
        Assert.Equal(purchaseBefore, await PurchaseSnapshotAsync(store, purchaseId));
        Assert.Equal(120, before.Count);
        Assert.Equal(29, before.Count(line => line.EndsWith("|Taken", StringComparison.Ordinal)));
    }

    private static async Task<List<string>> InventoryAsync(TestStore store)
    {
        await using var db = await store.Factory.CreateDbContextAsync();

        return await db.Seats
            .AsNoTracking()
            .OrderBy(seat => seat.Row)
            .ThenBy(seat => seat.Number)
            .Select(seat => $"{seat.Row}{seat.Number}|{seat.Price}|{seat.Status}")
            .ToListAsync();
    }

    /// <summary>
    /// The committed purchase with its timestamp, amount, and lines — the history a later start must
    /// leave exactly as it is (SC-007).
    /// </summary>
    private static async Task<List<string>> PurchaseSnapshotAsync(TestStore store, int purchaseId)
    {
        await using var db = await store.Factory.CreateDbContextAsync();

        var purchase = await db.Purchases
            .AsNoTracking()
            .SingleAsync(record => record.Id == purchaseId);

        var rows = new List<string>
        {
            $"purchase|{purchase.Id}|{purchase.CreatedAt:O}|{purchase.TotalAmount}"
        };

        var lines = await db.PurchaseLines
            .AsNoTracking()
            .Where(line => line.PurchaseId == purchaseId)
            .OrderBy(line => line.SeatId)
            .Select(line => $"{line.SeatId}|{line.PricePaid}")
            .ToListAsync();

        rows.AddRange(lines.Select(line => $"line|{line}"));

        return rows;
    }
}
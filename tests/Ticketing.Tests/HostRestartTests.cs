using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ticketing.Data;
using Ticketing.Models;
using Ticketing.Services;

namespace Ticketing.Tests;

/// <summary>
/// SC-001, SC-002, SC-008 and SC-009: the real host is built, disposed, and built again over the same
/// store file — migrations, seeding, and dependency injection included (research R10).
/// </summary>
public sealed class HostRestartTests
{
    [Fact]
    public async Task A_purchase_and_the_venue_survive_a_real_host_restart()
    {
        await using var store = new TestStore();

        int purchaseId;
        decimal expectedTotal;
        HashSet<string> takenBefore;
        string boughtSeatLabel;
        IReadOnlyList<string> stateBefore;

        using (var host = new TicketingAppFactory(store.StorePath))
        {
            using var client = host.CreateClient(); // starts the host: migrations and seeding run here

            Assert.True(File.Exists(store.StorePath), "The host must use the test's store file.");

            using var scope = host.Services.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<TicketingService>();
            await service.LoadAsync();

            var seats = service.Rows
                .SelectMany(row => row)
                .Where(seat => seat.State == SeatState.Available)
                .Take(2)
                .ToList();

            Assert.Equal(2, seats.Count);

            foreach (var seat in seats)
            {
                await service.ToggleSeat(seat);
            }

            expectedTotal = seats.Sum(seat => seat.Price);
            boughtSeatLabel = seats[0].Label;

            var outcome = await service.PurchaseAsync();
            Assert.Equal(PurchaseOutcomeKind.Succeeded, outcome.Kind);
            purchaseId = outcome.PurchaseId!.Value;

            takenBefore = TakenSeatIds(service);
            Assert.Equal(30, takenBefore.Count); // the 28 seeded seats plus the two just bought

            // The whole stored state — event, all seats with prices and statuses, every purchase and
            // its lines — captured before the restart, so FR-009 can be compared value for value.
            stateBefore = await StoreSnapshotAsync(store);
            Assert.Equal(120, stateBefore.Count(line => line.StartsWith("seat|", StringComparison.Ordinal)));
            Assert.Single(stateBefore, line => line.StartsWith("purchase|", StringComparison.Ordinal));
        }

        using (var host = new TicketingAppFactory(store.StorePath))
        {
            using var client = host.CreateClient();

            using var scope = host.Services.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<TicketingService>();
            await service.LoadAsync();

            // SC-001: not a single taken seat was lost across the restart.
            Assert.Equal(takenBefore, TakenSeatIds(service));

            // FR-001, FR-002, FR-003, FR-005, FR-009: the event, all 120 seats with their prices and
            // statuses, and every purchase with its timestamp, amount, and lines are byte-for-byte
            // the same state the store held before the restart.
            Assert.Equal(stateBefore, await StoreSnapshotAsync(store));

            // SC-002: the purchase still returns the same seats and the same amount.
            var receipt = await service.GetReceiptAsync(purchaseId);
            Assert.NotNull(receipt);
            Assert.Equal(expectedTotal, receipt!.Total);
            Assert.Equal(2, receipt.Seats.Count);
            Assert.Contains(receipt.Seats, line => line.Label == boughtSeatLabel);

            // The served page no longer offers the bought seat, and still shows the stored event.
            var html = await client.GetStringAsync("/");
            Assert.Contains($"{boughtSeatLabel}, occupied", html);
            Assert.Contains(TicketingSeeder.EventName, html);
        }
    }

    [Fact]
    public async Task An_unreadable_store_reports_an_error_and_shows_no_seats()
    {
        await using var store = new TestStore();
        await File.WriteAllTextAsync(store.StorePath, "this is not a database");

        using var host = new TicketingAppFactory(store.StorePath);
        using var client = host.CreateClient();

        var html = await client.GetStringAsync("/");

        Assert.Contains("The venue is not available", html);
        Assert.DoesNotContain("seat-map", html);
        Assert.DoesNotContain("Row A, Seat", html);
    }

    /// <summary>
/// Every value the store holds, as comparable text: the event, all seats with prices and statuses,
/// and every purchase with its timestamp, total, and lines (FR-001, FR-002, FR-003, FR-005, FR-009).
/// </summary>
private static async Task<IReadOnlyList<string>> StoreSnapshotAsync(TestStore store)
{
    await using var db = await store.Factory.CreateDbContextAsync();

    var rows = new List<string>();

    var eventRecord = await db.Events.AsNoTracking().SingleAsync();
    rows.Add(
        $"event|{eventRecord.Id}|{eventRecord.Name}|{eventRecord.Artist}|{eventRecord.StartsAt:O}|{eventRecord.Venue}");

    var seats = await db.Seats
        .AsNoTracking()
        .OrderBy(seat => seat.Row)
        .ThenBy(seat => seat.Number)
        .ToListAsync();

    rows.AddRange(seats.Select(seat => $"seat|{seat.Row}{seat.Number}|{seat.Price}|{seat.Status}"));

    var purchases = await db.Purchases
        .AsNoTracking()
        .OrderBy(purchase => purchase.Id)
        .ToListAsync();

    foreach (var purchase in purchases)
    {
        rows.Add($"purchase|{purchase.Id}|{purchase.CreatedAt:O}|{purchase.TotalAmount}");

        var lines = await db.PurchaseLines
            .AsNoTracking()
            .Where(line => line.PurchaseId == purchase.Id)
            .OrderBy(line => line.SeatId)
            .ToListAsync();

        rows.AddRange(lines.Select(line => $"line|{line.SeatId}|{line.PricePaid}"));
    }

    return rows;
}

private static HashSet<string> TakenSeatIds(TicketingService service) =>
        service.Rows
            .SelectMany(row => row)
            .Where(seat => seat.State == SeatState.Occupied)
            .Select(seat => seat.Id)
            .ToHashSet(StringComparer.Ordinal);
}
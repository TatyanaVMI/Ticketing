using Microsoft.Extensions.DependencyInjection;
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
        }

        using (var host = new TicketingAppFactory(store.StorePath))
        {
            using var client = host.CreateClient();

            using var scope = host.Services.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<TicketingService>();
            await service.LoadAsync();

            // SC-001: not a single taken seat was lost across the restart.
            Assert.Equal(takenBefore, TakenSeatIds(service));

            // SC-002: the purchase still returns the same seats and the same amount.
            var receipt = await service.GetReceiptAsync(purchaseId);
            Assert.NotNull(receipt);
            Assert.Equal(expectedTotal, receipt!.Total);
            Assert.Equal(2, receipt.Seats.Count);
            Assert.Contains(receipt.Seats, line => line.Label == boughtSeatLabel);

            // The served page no longer offers the bought seat.
            var html = await client.GetStringAsync("/");
            Assert.Contains($"{boughtSeatLabel}, occupied", html);
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

    private static HashSet<string> TakenSeatIds(TicketingService service) =>
        service.Rows
            .SelectMany(row => row)
            .Where(seat => seat.State == SeatState.Occupied)
            .Select(seat => seat.Id)
            .ToHashSet(StringComparer.Ordinal);
}
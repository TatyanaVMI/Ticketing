using Microsoft.EntityFrameworkCore;
using Ticketing.Data;
using Ticketing.Models;
using Ticketing.Services;

namespace Ticketing.Tests;

/// <summary>
/// SC-003 / FR-010: every visitor reads the same stored venue, and a page that has not read it again
/// can never buy from what it still shows (service contract §5).
/// </summary>
public sealed class SharedVenueTests
{
    [Fact]
    public async Task Two_visitors_who_read_the_venue_agree_on_every_seat_after_a_purchase()
    {
        await using var store = new TestStore();
        await store.SeedAsync();

        var buyer = await store.CreateServiceAsync();
        var other = await store.CreateServiceAsync();

        var seat = buyer.Rows
            .SelectMany(row => row)
            .First(candidate => candidate.State == SeatState.Available);

        await buyer.ToggleSeat(seat);
        Assert.Equal(PurchaseOutcomeKind.Succeeded, (await buyer.PurchaseAsync()).Kind);

        await other.LoadAsync();

        Assert.Equal(Snapshot(buyer), Snapshot(other));
        Assert.Equal(SeatState.Occupied, other.Rows.SelectMany(row => row).Single(candidate => candidate.Id == seat.Id).State);
    }

    [Fact]
    public async Task A_stale_page_cannot_choose_a_seat_that_is_taken_in_the_store()
    {
        await using var store = new TestStore();
        await store.SeedAsync();

        var buyer = await store.CreateServiceAsync();
        var stale = await store.CreateServiceAsync();

        var seat = buyer.Rows
            .SelectMany(row => row)
            .First(candidate => candidate.State == SeatState.Available);

        var staleSeat = stale.Rows.SelectMany(row => row).Single(candidate => candidate.Id == seat.Id);

        await buyer.ToggleSeat(seat);
        await buyer.PurchaseAsync();

        Assert.Equal(SeatState.Available, staleSeat.State);

        await stale.ToggleSeat(staleSeat);

        Assert.Empty(stale.SelectedSeats);
        Assert.Equal(SeatState.Occupied, staleSeat.State);
    }

    [Fact]
    public async Task A_refused_purchase_changes_no_seat_anywhere()
    {
        await using var store = new TestStore();
        await store.SeedAsync();

        var buyer = await store.CreateServiceAsync();

        var seat = buyer.Rows
            .SelectMany(row => row)
            .First(candidate => candidate.State == SeatState.Available);

        await buyer.ToggleSeat(seat);

        // Another visitor takes the same seat directly in the store.
        await using (var db = await store.Factory.CreateDbContextAsync())
        {
            var record = await db.Seats.SingleAsync(candidate => candidate.Row == seat.Row && candidate.Number == seat.Number);
            record.Status = SeatStatus.Taken;
            await db.SaveChangesAsync();
        }

        var inventoryBefore = await StoreSnapshotAsync(store);

        var outcome = await buyer.PurchaseAsync();

        Assert.Equal(PurchaseOutcomeKind.SeatNoLongerAvailable, outcome.Kind);
        Assert.Empty(buyer.SelectedSeats);
        Assert.Equal(inventoryBefore, await StoreSnapshotAsync(store));
        Assert.Equal(SeatState.Occupied, buyer.Rows.SelectMany(row => row).Single(candidate => candidate.Id == seat.Id).State);

        await using var check = await store.Factory.CreateDbContextAsync();
        Assert.Equal(0, await check.Purchases.CountAsync());
    }

    [Fact]
    public async Task A_refused_purchase_keeps_the_other_selected_seats()
    {
        await using var store = new TestStore();
        await store.SeedAsync();

        var buyer = await store.CreateServiceAsync();
        var seats = buyer.Rows
            .SelectMany(row => row)
            .Where(seat => seat.State == SeatState.Available)
            .Take(2)
            .ToList();

        foreach (var seat in seats)
        {
            await buyer.ToggleSeat(seat);
        }

        // Another visitor takes one of the two chosen seats directly in the store.
        await using (var db = await store.Factory.CreateDbContextAsync())
        {
            var record = await db.Seats.SingleAsync(candidate => candidate.Row == seats[0].Row && candidate.Number == seats[0].Number);
            record.Status = SeatStatus.Taken;
            await db.SaveChangesAsync();
        }

        var outcome = await buyer.PurchaseAsync();

        // The refused seat is dropped, the rest of the selection is left intact (service contract §3).
        Assert.Equal(PurchaseOutcomeKind.SeatNoLongerAvailable, outcome.Kind);
        var remaining = Assert.Single(buyer.SelectedSeats);
        Assert.Equal(seats[1].Id, remaining.Id);
        Assert.Equal(SeatState.Selected, remaining.State);

        var visible = buyer.Rows.SelectMany(row => row).ToList();
        Assert.Equal(SeatState.Occupied, visible.Single(seat => seat.Id == seats[0].Id).State);
        Assert.Equal(SeatState.Selected, visible.Single(seat => seat.Id == seats[1].Id).State);

        await using var check = await store.Factory.CreateDbContextAsync();
        Assert.Equal(0, await check.Purchases.CountAsync());
    }

    [Fact]
    public async Task A_selection_belongs_to_one_visitor_and_is_never_stored()
    {
        await using var store = new TestStore();
        await store.SeedAsync();

        var visitor = await store.CreateServiceAsync();
        var seat = visitor.Rows
            .SelectMany(row => row)
            .First(candidate => candidate.State == SeatState.Available);

        await visitor.ToggleSeat(seat);

        Assert.Single(visitor.SelectedSeats);
        Assert.Equal(SeatState.Selected, seat.State);

        // A fresh circuit over the same store sees the seat as free: selection is session state (FR-013).
        var reopened = await store.CreateServiceAsync();
        var reopenedSeat = reopened.Rows.SelectMany(row => row).Single(candidate => candidate.Id == seat.Id);

        Assert.Equal(SeatState.Available, reopenedSeat.State);
        Assert.Empty(reopened.SelectedSeats);

        await using var db = await store.Factory.CreateDbContextAsync();
        var record = await db.Seats
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Row == seat.Row && candidate.Number == seat.Number);

        Assert.Equal(SeatStatus.Available, record.Status);
    }

    private static IReadOnlyList<string> Snapshot(TicketingService service) =>
        service.Rows
            .SelectMany(row => row)
            .Select(seat => $"{seat.Id}|{seat.State}|{seat.Price}")
            .ToList();

    private static async Task<IReadOnlyList<string>> StoreSnapshotAsync(TestStore store)
    {
        await using var db = await store.Factory.CreateDbContextAsync();

        return await db.Seats
            .AsNoTracking()
            .OrderBy(seat => seat.Row)
            .ThenBy(seat => seat.Number)
            .Select(seat => $"{seat.Row}{seat.Number}|{seat.Price}|{seat.Status}")
            .ToListAsync();
    }
}
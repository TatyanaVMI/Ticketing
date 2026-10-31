using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Ticketing.Data;
using Ticketing.Models;
using Ticketing.Services;

namespace Ticketing.Tests;

/// <summary>
/// SC-004 / SC-005 and FR-005…FR-008: a purchase is one unit, its history is frozen, a taken seat is
/// not sold twice, and a failure leaves nothing behind.
/// </summary>
public sealed class PurchaseStoreTests
{
    [Fact]
    public async Task A_purchase_writes_the_record_its_lines_and_the_seat_statuses_together()
    {
        await using var store = new TestStore();
        await store.SeedAsync();
        var service = await store.CreateServiceAsync();

        var seats = AvailableSeats(service, 2);
        foreach (var seat in seats)
        {
            await service.ToggleSeat(seat);
        }

        var expectedTotal = seats.Sum(seat => seat.Price);
        var outcome = await service.PurchaseAsync();

        Assert.Equal(PurchaseOutcomeKind.Succeeded, outcome.Kind);
        Assert.NotNull(outcome.PurchaseId);

        await using var db = await store.Factory.CreateDbContextAsync();
        var purchase = Assert.Single(await db.Purchases.AsNoTracking().Include(record => record.Lines).ToListAsync());

        Assert.Equal(expectedTotal, purchase.TotalAmount);
        Assert.NotEqual(default, purchase.CreatedAt);
        Assert.Equal(2, purchase.Lines.Count);
        Assert.Equal(expectedTotal, purchase.Lines.Sum(line => line.PricePaid));

        var purchasedSeatIds = purchase.Lines.Select(line => line.SeatId).ToList();
        var purchasedSeats = await db.Seats
            .AsNoTracking()
            .Where(seat => purchasedSeatIds.Contains(seat.Id))
            .ToListAsync();

        Assert.Equal(2, purchasedSeats.Count);
        Assert.All(purchasedSeats, seat => Assert.Equal(SeatStatus.Taken, seat.Status));

        // The confirmation is the record read back out of the store (FR-014).
        var receipt = await service.GetReceiptAsync(outcome.PurchaseId!.Value);

        Assert.NotNull(receipt);
        Assert.Equal(expectedTotal, receipt!.Total);
        Assert.Equal(purchase.CreatedAt, receipt.CreatedAt);
        Assert.NotEqual(default, receipt.CreatedAt);
        Assert.Equal(
            seats.Select(seat => seat.Label).OrderBy(label => label, StringComparer.Ordinal),
            receipt.Seats.Select(line => line.Label).OrderBy(label => label, StringComparer.Ordinal));
        Assert.All(
            seats,
            seat => Assert.Equal(seat.Price, receipt.Seats.Single(line => line.Label == seat.Label).PricePaid));
    }

    [Fact]
    public async Task Changing_a_price_later_does_not_rewrite_a_committed_purchase()
    {
        await using var store = new TestStore();
        await store.SeedAsync();
        var service = await store.CreateServiceAsync();

        var seat = AvailableSeats(service, 1)[0];
        await service.ToggleSeat(seat);
        var outcome = await service.PurchaseAsync();
        Assert.Equal(PurchaseOutcomeKind.Succeeded, outcome.Kind);

        await using (var db = await store.Factory.CreateDbContextAsync())
        {
            var record = await db.Seats.SingleAsync(candidate =>
                candidate.Row == seat.Row && candidate.Number == seat.Number);
            record.Price = 999m;
            await db.SaveChangesAsync();
        }

        var receipt = await service.GetReceiptAsync(outcome.PurchaseId!.Value);

        Assert.NotNull(receipt);
        Assert.Equal(seat.Price, receipt!.Total);
        Assert.Equal(seat.Price, Assert.Single(receipt.Seats).PricePaid);
    }

    [Fact]
    public async Task A_seat_recorded_as_taken_cannot_be_bought_again_sequentially()
    {
        await using var store = new TestStore();
        await store.SeedAsync();

        var buyer = await store.CreateServiceAsync();
        var bystander = await store.CreateServiceAsync();

        var seat = AvailableSeats(buyer, 1)[0];
        var staleSeat = bystander.Rows.SelectMany(row => row).Single(candidate => candidate.Id == seat.Id);

        await buyer.ToggleSeat(seat);
        Assert.Equal(PurchaseOutcomeKind.Succeeded, (await buyer.PurchaseAsync()).Kind);

        // The bystander's screen still shows the seat as free; the store refuses it anyway.
        Assert.Equal(SeatState.Available, staleSeat.State);
        await bystander.ToggleSeat(staleSeat);

        Assert.Empty(bystander.SelectedSeats);
        Assert.Equal(SeatState.Occupied, staleSeat.State);
        Assert.Equal(PurchaseOutcomeKind.NothingSelected, (await bystander.PurchaseAsync()).Kind);

        await using var db = await store.Factory.CreateDbContextAsync();
        Assert.Equal(1, await db.Purchases.CountAsync());
        Assert.Equal(1, await db.PurchaseLines.CountAsync());
    }

    [Fact]
    public async Task A_failure_in_the_middle_of_a_purchase_leaves_nothing_behind()
    {
        await using var store = new TestStore();
        await store.SeedAsync();
        var service = await store.CreateServiceAsync();

        var seats = AvailableSeats(service, 2);
        foreach (var seat in seats)
        {
            await service.ToggleSeat(seat);
        }

        // Forces the insert of the first purchase line to fail, after the purchase row was written.
        await using (var db = await store.Factory.CreateDbContextAsync())
        {
            await db.Database.ExecuteSqlRawAsync(
                """
                CREATE TRIGGER fail_purchase_line
                BEFORE INSERT ON PurchaseLines
                BEGIN
                    SELECT RAISE(ABORT, 'forced failure');
                END;
                """);
        }

        await Assert.ThrowsAnyAsync<Exception>(() => service.PurchaseAsync());

        await using var check = await store.Factory.CreateDbContextAsync();

        Assert.Equal(0, await check.Purchases.CountAsync());
        Assert.Equal(0, await check.PurchaseLines.CountAsync());

        var seatIds = seats.Select(seat => seat.Id).ToList();
        var untouched = (await check.Seats.AsNoTracking().ToListAsync())
            .Where(record => seatIds.Contains($"{record.Row}{record.Number}"))
            .ToList();

        Assert.Equal(2, untouched.Count);
        Assert.All(untouched, seat => Assert.Equal(SeatStatus.Available, seat.Status));
    }

    [Fact]
    public async Task A_purchase_that_was_never_recorded_has_no_receipt()
    {
        await using var store = new TestStore();
        await store.SeedAsync();
        var service = await store.CreateServiceAsync();

        Assert.Null(await service.GetReceiptAsync(int.MaxValue));
    }

    [Fact]
    public async Task A_duplicate_submission_cannot_disturb_a_running_purchase()
    {
        await using var store = new TestStore();
        await store.SeedAsync();

        var gate = new GatedContextFactory(store.Factory);
        var service = new TicketingService(gate, NullLogger<TicketingService>.Instance);
        await service.LoadAsync();

        var seats = AvailableSeats(service, 2);
        foreach (var seat in seats)
        {
            await service.ToggleSeat(seat);
        }

        gate.Arm();
        var first = service.PurchaseAsync();
        Assert.True(service.IsPurchasing);

        // A second Buy click and a seat click while the first attempt is in flight are both ignored,
        // so the store never receives a second transaction or an emptied selection.
        var second = await service.PurchaseAsync();
        await service.ToggleSeat(seats[0]);

        Assert.Equal(PurchaseOutcomeKind.NothingSelected, second.Kind);
        Assert.Equal(2, service.SelectedCount);
        Assert.All(seats, seat => Assert.Equal(SeatState.Selected, seat.State));

        gate.Release();
        var outcome = await first;

        Assert.Equal(PurchaseOutcomeKind.Succeeded, outcome.Kind);
        Assert.False(service.IsPurchasing);

        await using var db = await store.Factory.CreateDbContextAsync();
        var purchase = Assert.Single(await db.Purchases.AsNoTracking().Include(record => record.Lines).ToListAsync());

        Assert.Equal(2, purchase.Lines.Count);
        Assert.Equal(seats.Sum(seat => seat.Price), purchase.TotalAmount);
    }

    private static List<Seat> AvailableSeats(TicketingService service, int count) =>
        service.Rows
            .SelectMany(row => row)
            .Where(seat => seat.State == SeatState.Available)
            .Take(count)
            .ToList();

    /// <summary>Holds the first context creation after <see cref="Arm"/> so a purchase can be paused mid-flight.</summary>
    private sealed class GatedContextFactory(IDbContextFactory<TicketingDbContext> inner)
        : IDbContextFactory<TicketingDbContext>
    {
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _armed;
        private int _entered;

        public void Arm() => Interlocked.Exchange(ref _armed, 1);

        public void Release() => _gate.TrySetResult();

        public TicketingDbContext CreateDbContext() => inner.CreateDbContext();

        public async Task<TicketingDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            if (Volatile.Read(ref _armed) == 1 && Interlocked.Increment(ref _entered) == 1)
            {
                await _gate.Task.WaitAsync(cancellationToken);
            }

            return await inner.CreateDbContextAsync(cancellationToken);
        }
    }
}
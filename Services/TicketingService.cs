using Microsoft.EntityFrameworkCore;
using Ticketing.Data;
using Ticketing.Models;

namespace Ticketing.Services;

/// <summary>
/// The only door between the components and the store (constitution Principle II). Scoped per
/// circuit: the seat selection belongs to this visitor, everything else is read from the store.
/// </summary>
public sealed class TicketingService
{
    private readonly IDbContextFactory<TicketingDbContext> _contextFactory;
    private readonly ILogger<TicketingService> _logger;
    private readonly List<Seat> _seats = new();
    private readonly HashSet<string> _selected = new(StringComparer.Ordinal);
    private int _eventId;

    public TicketingService(IDbContextFactory<TicketingDbContext> contextFactory, ILogger<TicketingService> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    public ConcertEvent? Event { get; private set; }

    public bool IsLoaded { get; private set; }

    /// <summary>Set when the venue could not be read. While it is set, the UI must not render seats.</summary>
    public string? LoadError { get; private set; }

    public IEnumerable<IGrouping<string, Seat>> Rows => _seats.GroupBy(seat => seat.Row);

    public IReadOnlyList<Seat> SelectedSeats => _seats.Where(seat => seat.State == SeatState.Selected).ToList();

    public int SelectedCount => SelectedSeats.Count;

    public decimal TotalPrice => SelectedSeats.Sum(seat => seat.Price);

    public bool CanBuy => SelectedCount > 0;

    public event Action? OnChange;

    /// <summary>
    /// Reads the event and the whole seat inventory from the store and applies this circuit's
    /// selection overlay. Idempotent; sets <see cref="IsLoaded"/> or <see cref="LoadError"/>.
    /// </summary>
    public async Task LoadAsync()
    {
        try
        {
            await using var db = await _contextFactory.CreateDbContextAsync();

            var eventRecord = await db.Events
                .AsNoTracking()
                .OrderBy(record => record.Id)
                .FirstOrDefaultAsync();
            if (eventRecord is null)
            {
                Fail("The venue has not been created yet, so no seats can be shown.");
                return;
            }

            var seatRecords = await db.Seats
                .AsNoTracking()
                .Where(seat => seat.EventId == eventRecord.Id)
                .OrderBy(seat => seat.Row)
                .ThenBy(seat => seat.Number)
                .ToListAsync();

            _eventId = eventRecord.Id;
            Event = new ConcertEvent
            {
                Name = eventRecord.Name,
                Artist = eventRecord.Artist,
                Date = eventRecord.StartsAt,
                Venue = eventRecord.Venue
            };

            ApplySeatSnapshot(seatRecords);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "The seat inventory could not be read from the store.");
            Fail("The venue cannot be shown because the seat inventory could not be read.");
            return;
        }

        IsLoaded = true;
        LoadError = null;
        NotifyStateChanged();
    }

    /// <summary>
    /// Adds or removes a seat from this circuit's selection. A seat that is taken in the store is
    /// refused even while the local view still shows it free, and the local view is corrected
    /// (service contract §3 and §5).
    /// </summary>
    public async Task ToggleSeat(Seat seat)
    {
        ArgumentNullException.ThrowIfNull(seat);

        if (_selected.Remove(seat.Id))
        {
            seat.State = SeatState.Available;
            NotifyStateChanged();
            return;
        }

        if (seat.State == SeatState.Occupied)
        {
            return;
        }

        bool taken;
        try
        {
            taken = await IsTakenInStoreAsync(seat);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "The status of seat {Seat} could not be confirmed against the store.", seat.Id);
            Fail("The venue cannot be shown because the seat inventory could not be read.");
            return;
        }

        if (taken)
        {
            // The page lagged behind the store: refuse the seat and show it as taken.
            seat.State = SeatState.Occupied;
            NotifyStateChanged();
            return;
        }

        _selected.Add(seat.Id);
        seat.State = SeatState.Selected;
        NotifyStateChanged();
    }

    /// <summary>
    /// Commits the selection as one purchase. Everything happens inside one transaction: either the
    /// purchase, its lines, and the new seat statuses are written together, or nothing is written
    /// (spec FR-008, SC-005).
    /// </summary>
    public async Task<PurchaseOutcome> PurchaseAsync()
    {
        if (!CanBuy)
        {
            return PurchaseOutcome.NothingSelected;
        }

        try
        {
            await using var db = await _contextFactory.CreateDbContextAsync();
            await using var transaction = await db.Database.BeginTransactionAsync();

            var seatRecords = await db.Seats
                .Where(seat => seat.EventId == _eventId)
                .OrderBy(seat => seat.Row)
                .ThenBy(seat => seat.Number)
                .ToListAsync();

            var selected = seatRecords
                .Where(record => _selected.Contains(Key(record.Row, record.Number)))
                .ToList();

            var taken = selected.Where(record => record.Status != SeatStatus.Available).ToList();

            // A selected seat is no longer on sale in the store, or has disappeared from it: write nothing.
            if (taken.Count > 0 || selected.Count != _selected.Count)
            {
                await transaction.RollbackAsync();
                ApplySeatSnapshot(seatRecords);
                NotifyStateChanged();
                return PurchaseOutcome.SeatNoLongerAvailable;
            }

            var purchase = new PurchaseRecord
            {
                EventId = _eventId,
                CreatedAt = DateTimeOffset.UtcNow,
                TotalAmount = selected.Sum(record => record.Price)
            };

            foreach (var record in selected)
            {
                purchase.Lines.Add(new PurchaseLineRecord { SeatId = record.Id, PricePaid = record.Price });
                record.Status = SeatStatus.Taken;
            }

            db.Purchases.Add(purchase);

            await db.SaveChangesAsync();
            await transaction.CommitAsync();

            _selected.Clear();
            ApplySeatSnapshot(seatRecords);
            NotifyStateChanged();

            return PurchaseOutcome.Succeeded(purchase.Id);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "The purchase could not be committed; the transaction was rolled back.");
            throw;
        }
    }

    /// <summary>
    /// Reads a committed purchase back out of the store, together with the seats it contains and the
    /// prices that were paid. Returns null when the purchase does not exist.
    /// </summary>
    public async Task<PurchaseReceipt?> GetReceiptAsync(int purchaseId)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();

        var purchase = await db.Purchases
            .AsNoTracking()
            .Where(record => record.Id == purchaseId)
            .Select(record => new { record.Id, record.CreatedAt, record.TotalAmount })
            .FirstOrDefaultAsync();

        if (purchase is null)
        {
            return null;
        }

        var lines = await db.PurchaseLines
            .AsNoTracking()
            .Where(line => line.PurchaseId == purchaseId)
            .Join(
                db.Seats.AsNoTracking(),
                line => line.SeatId,
                seat => seat.Id,
                (line, seat) => new { seat.Row, seat.Number, line.PricePaid })
            .ToListAsync();

        return new PurchaseReceipt
        {
            PurchaseId = purchase.Id,
            CreatedAt = purchase.CreatedAt,
            Total = purchase.TotalAmount,
            Seats = lines
                .OrderBy(line => line.Row)
                .ThenBy(line => line.Number)
                .Select(line => new PurchaseReceiptLine(Seat.LabelFor(line.Row, line.Number), line.PricePaid))
                .ToList()
        };
    }

    private async Task<bool> IsTakenInStoreAsync(Seat seat)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();

        return await db.Seats
            .AsNoTracking()
            .Where(record => record.EventId == _eventId && record.Row == seat.Row && record.Number == seat.Number)
            .Select(record => record.Status == SeatStatus.Taken)
            .SingleOrDefaultAsync();
    }

    /// <summary>
    /// Rebuilds the visible inventory from store rows and reapplies the selection overlay, dropping any
    /// chosen seat that is no longer on sale.
    /// </summary>
    private void ApplySeatSnapshot(List<SeatRecord> seatRecords)
    {
        _seats.Clear();

        foreach (var record in seatRecords)
        {
            _seats.Add(new Seat
            {
                Row = record.Row,
                Number = record.Number,
                Price = record.Price,
                State = record.Status == SeatStatus.Taken
                    ? SeatState.Occupied
                    : _selected.Contains(Key(record.Row, record.Number))
                        ? SeatState.Selected
                        : SeatState.Available
            });
        }

        var selectable = _seats
            .Where(seat => seat.State != SeatState.Occupied)
            .Select(seat => seat.Id)
            .ToHashSet(StringComparer.Ordinal);

        _selected.IntersectWith(selectable);
    }

    private void Fail(string message)
    {
        LoadError = message;
        IsLoaded = false;
        Event = null;
        _seats.Clear();
        _selected.Clear();
        NotifyStateChanged();
    }

    private void NotifyStateChanged() => OnChange?.Invoke();

    private static string Key(string row, int number) => $"{row}{number}";
}
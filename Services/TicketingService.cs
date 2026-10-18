using Ticketing.Models;

namespace Ticketing.Services;

public class TicketingService
{
    private const int RowCount = 10;
    private const int SeatsPerRow = 12;

    private readonly List<Seat> _seats = new();

    public TicketingService()
    {
        Event = new ConcertEvent
        {
            Name = "Midnight Frequencies Tour",
            Artist = "The Static Fields",
            Date = new DateTime(2026, 11, 21, 20, 0, 0),
            Venue = "Grand Hall Auditorium"
        };

        BuildSeats();
    }

    public ConcertEvent Event { get; }

    public IReadOnlyList<Seat> Seats => _seats;

    public IReadOnlyList<Seat> SelectedSeats =>
        _seats.Where(s => s.State == SeatState.Selected).ToList();

    public decimal TotalPrice => SelectedSeats.Sum(s => s.Price);

    public int SelectedCount => _seats.Count(s => s.State == SeatState.Selected);

    public bool CanBuy => SelectedCount > 0;

    public event Action? OnChange;

    public IEnumerable<IGrouping<string, Seat>> Rows =>
        _seats.GroupBy(s => s.Row);

    public void ToggleSeat(Seat seat)
    {
        if (seat.State == SeatState.Available)
        {
            seat.State = SeatState.Selected;
        }
        else if (seat.State == SeatState.Selected)
        {
            seat.State = SeatState.Available;
        }
        else
        {
            return;
        }

        NotifyStateChanged();
    }

    public void Purchase()
    {
        if (!CanBuy)
        {
            return;
        }

        foreach (var seat in SelectedSeats)
        {
            seat.State = SeatState.Occupied;
        }

        NotifyStateChanged();
    }

    private void NotifyStateChanged() => OnChange?.Invoke();

    private void BuildSeats()
    {
        var rng = new Random(20261121);

        for (var rowIndex = 0; rowIndex < RowCount; rowIndex++)
        {
            var row = ((char)('A' + rowIndex)).ToString();
            var price = PriceForRow(rowIndex);

            for (var number = 1; number <= SeatsPerRow; number++)
            {
                var roll = rng.Next(100);
                var state = roll switch
                {
                    < 22 => SeatState.Occupied,
                    < 32 => SeatState.Unavailable,
                    _ => SeatState.Available
                };

                _seats.Add(new Seat
                {
                    Row = row,
                    Number = number,
                    Price = price,
                    State = state
                });
            }
        }
    }

    private static decimal PriceForRow(int rowIndex) => rowIndex switch
    {
        0 => 150m,
        >= 1 and <= 3 => 120m,
        >= 4 and <= 7 => 90m,
        _ => 60m
    };
}

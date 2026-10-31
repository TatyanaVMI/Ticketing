namespace Ticketing.Models;

public class Seat
{
    public required string Row { get; init; }
    public required int Number { get; init; }
    public required decimal Price { get; init; }
    public SeatState State { get; set; }

    public string Id => $"{Row}{Number}";

    public string Label => LabelFor(Row, Number);

    public bool IsSelectable => State is SeatState.Available or SeatState.Selected;

    public static string LabelFor(string row, int number) => $"Row {row}, Seat {number}";
}
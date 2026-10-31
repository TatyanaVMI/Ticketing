namespace Ticketing.Models;

/// <summary>
/// The states a visitor can see on the map. Only <see cref="Available"/> and <see cref="Occupied"/>
/// correspond to a stored status; <see cref="Selected"/> is this circuit's unsaved choice.
/// </summary>
public enum SeatState
{
    Available,
    Selected,
    Occupied
}
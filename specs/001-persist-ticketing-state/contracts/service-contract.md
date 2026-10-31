# Contract: the state layer the UI is allowed to use

Application type: a server-rendered web application. Its meaningful interface is therefore not HTTP —
it is the surface the components are allowed to call, and the guarantees that surface must keep. This
document is that contract.

Applies to `Services/TicketingService.cs` (scoped per circuit).

---

## 1. Access rules

1. Components may depend on `TicketingService` only. They MUST NOT reference `Data/`, `TicketingDbContext`,
   or any `*Record` type — not even for a read. The service is the only door (Constitution Principle II).
2. Components MUST NOT keep authoritative copies of seat status, prices, or totals between renders. A
   value handed to a component is read-only and may be re-read at any time.
3. Only the service writes. A component signals intent (`ToggleSeat`, `PurchaseAsync`) and re-renders
   from what the service exposes.

## 2. Read surface

| Member | Contract |
|---|---|
| `LoadAsync()` | Loads the event and the seat inventory and applies the current selection overlay. Idempotent. Sets `IsLoaded` or `LoadError`. |
| `IsLoaded` | True when a venue was read successfully and may be shown. |
| `LoadError` | Non-null when the venue could not be read. When it is set, the UI MUST NOT render seats, the selection summary, or the buy action (FR-015, SC-008). |
| `Event` | The current event, or null before a successful load. |
| `Rows` | Seats grouped by row, in row order, each seat carrying its current visible state. Read-only. |
| `SelectedSeats` | This circuit's chosen seats, in stable order. Empty until the visitor chooses. |
| `SelectedCount`, `TotalPrice` | Derived from `SelectedSeats` and the stored prices. Never cached across a grab-bag of renders. |
| `CanBuy` | True when at least one seat is selected. |
| `OnChange` | Raised whenever anything a visitor can see has changed **in this circuit**. Components re-render on it and unsubscribe when disposed. |

## 3. Write surface

| Member | Contract |
|---|---|
| `Task ToggleSeat(Seat seat)` | Adds or removes a seat from this circuit's selection, **only after confirming against the store** that the seat is not taken. A taken seat is refused and removed from the selection if it was there. Selection is circuit-local and is never written to the store (FR-013). |
| `Task<PurchaseOutcome> PurchaseAsync()` | Commits the selection as one purchase. See below. |

### `PurchaseOutcome`

| Value | When | What the UI does |
|---|---|---|
| `NothingSelected` | `CanBuy` was false; no store change | Do nothing |
| `SeatNoLongerAvailable` | A selected seat was taken before the transaction committed; **nothing** was written | Tell the visitor the seat went away, drop that seat from the selection, leave the rest selectable |
| `Succeeded` | Purchase, its lines, and the seat statuses were committed together | Re-read the purchase and show the confirmation |

`Succeeded` carries the new purchase's identifier and nothing else: the confirmation is built from the
**record read back out of the store**, never from values the caller passed in (FR-014, Constitution
Principle II).

## 4. Guarantees

1. **Atomicity** — a purchase is one unit: purchase, lines, and seat statuses, or nothing (FR-008,
   SC-005).
2. **Immutability of history** — a committed purchase is never modified afterwards, and the amount is
   never recomputed from current prices (FR-006).
3. **Single authority** — every value the UI shows comes from the store, so two visitors who have each
   read the venue cannot hold incompatible views of it (FR-010, SC-003).
4. **No durable selection** — an unsaved selection never reaches the store, so a restart simply empties
   it, without error (FR-013).
5. **Failure is visible** — a store that cannot be read yields `LoadError`, never a venue that looks
   fully available (FR-015, SC-008).

## 5. Freshness

There is no push between visitors, and the service exposes none. Contract:

- A page shows the venue as of its last read. Nothing tells it that another visitor bought something.
- `ToggleSeat` and `PurchaseAsync` MUST therefore re-check the store rather than trust what is on screen:
  a seat that is taken in the store is refused even while the local view still shows it free.
- The UI MUST NOT present a stale view as current. It may be renewed by reading again, but it MUST NOT
  imply a live connection that does not exist.
- `OnChange` is raised only inside the circuit whose own action caused it.

Why no push is in [research.md](../research.md) R7: the spec's authority requirement (FR-010) is already
met by reading the store, while the freshness a notifier would add cannot be measured by SC-003 and would
be the least testable part of the design.

## 6. Out of contract (deliberately)

- No cancellation, refund, or seat release.
- No visitor identity, session token, or "my purchases" lookup (FR-018: nothing is presented at the
  door).
- No protection against two *different* visitors buying the same seat in the same instant; the
  guarantee is sequential (spec Assumptions, SC-004).

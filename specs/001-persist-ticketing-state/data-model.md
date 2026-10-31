# Phase 1 Data Model: Persist Ticketing State

The persistent shape of the venue, and how it is projected onto the types the UI already uses.

Two vocabularies are in play, deliberately:

| Spec wording (plain language) | Code name | Meaning |
|---|---|---|
| available | `SeatStatus.Available` (stored) / `SeatState.Available` (shown) | On sale. |
| taken | `SeatStatus.Taken` (stored) / `SeatState.Occupied` (shown) | Sold. Cannot be bought again (FR-007). |
| selected | `SeatState.Selected` (shown only) | This circuit's unsaved choice (FR-013). Never stored. |

The stored vocabulary is the truthful one ("taken" means a purchase took it); the shown vocabulary
keeps the word the legend has always used, so the visible UI does not change (FR-016). There is no
stored "unavailable" state any more.

---

## Stored entities

### `EventRecord` — the performance being sold

| Field | Type | Notes |
|---|---|---|
| `Id` | `int` | Primary key. |
| `Name` | `string` | Required, e.g. `Midnight Frequencies Tour`. |
| `Artist` | `string` | Required. |
| `StartsAt` | `DateTimeOffset` | Required. Stored with offset so "when it happens" is unambiguous (FR-001, FR-005). |
| `Venue` | `string` | Required. |

Exactly one event exists in this iteration (Assumptions). No uniqueness constraint is imposed on the
name, because a second event is out of scope, and a constraint that no requirement asks for would be
speculation.

### `SeatRecord` — one seat in the venue

| Field | Type | Notes |
|---|---|---|
| `Id` | `int` | Primary key. |
| `EventId` | `int` | Foreign key → `EventRecord.Id`, required, cascade delete. |
| `Row` | `string` | Required, single letter `A`–`J`. |
| `Number` | `int` | Required, 1–12. |
| `Price` | `decimal` | Required. The price this seat is sold at (FR-003). |
| `Status` | `SeatStatus` | Required. Stored as text: `Available` or `Taken`. |

**Constraints**

- Unique index on (`EventId`, `Row`, `Number`) — one seat per position per event (FR-002, SC-007: no
  duplicate seats can exist).
- `Status` is stored as text, not as an integer: the enum's membership has already changed once
  (an "unavailable" member was removed), and integer storage would have silently re-labelled stored
  rows when that happened.

**State transitions**

```
Available ──(purchase commits)──► Taken
Taken     ──(never)──────────────► Available
```

One-way only (FR-007, FR-009). "Selected" is not a transition of this entity — it is a per-circuit
overlay that never touches the store (FR-013). There is no cancellation, refund, or release in this
iteration.

### `PurchaseRecord` — one completed transaction

| Field | Type | Notes |
|---|---|---|
| `Id` | `int` | Primary key. Internal only: the spec deliberately gives the visitor nothing to present (FR-018). |
| `EventId` | `int` | Foreign key → `EventRecord.Id`, required, restrict delete. |
| `CreatedAt` | `DateTimeOffset` | Required. When the purchase happened (FR-005). |
| `TotalAmount` | `decimal` | Required. The amount paid, fixed at purchase time (FR-006). |

**Constraints**

- `TotalAmount` MUST equal the sum of its lines' `PricePaid` (FR-006). Enforced by the code that
  writes it and asserted by a test; not by a database trigger, because a trigger would duplicate the
  rule in a second language.
- Deleting a purchase is not a supported operation in this iteration; the foreign key will not cascade
  from events for the same reason (nothing deletes events either).

### `PurchaseLineRecord` — the link between a purchase and one seat

| Field | Type | Notes |
|---|---|---|
| `Id` | `int` | Primary key. |
| `PurchaseId` | `int` | Foreign key → `PurchaseRecord.Id`, required, cascade delete. |
| `SeatId` | `int` | Foreign key → `SeatRecord.Id`, required, restrict delete. |
| `PricePaid` | `decimal` | Required. The price of that seat at the moment of purchase. |

**Constraints**

- Unique index on (`PurchaseId`, `SeatId`) — the same seat cannot appear twice in one purchase.
- `PricePaid` is copied, not looked up: a later change to `SeatRecord.Price` must not rewrite history
  (FR-006, and the "price of a row changes after seats have been sold" edge case).

**Why the line exists at all**: it is what makes a purchase answerable after a restart — which seats,
at which price, in one transaction. Collapsing it into a single amount would satisfy a receipt but
would make SC-002 ("the same seats and the same amount") unverifiable.

---

## Relationship overview

```
EventRecord 1 ──── * SeatRecord
     │ 1                    │ 1
     │                      │
     │ *                    │ *
PurchaseRecord 1 ── * PurchaseLineRecord * ── 1 (restrict)
```

A purchase belongs to an event; its lines point at seats of that event. Nothing links a purchase to a
visitor — there is no visitor identity (Assumptions).

---

## Seeding rules (FR-011, FR-012, SC-006)

Seeding runs on start, after migrations, and only when the store has no event:

1. If an event row exists, do nothing. Do not add, update, or reset a single seat (FR-011, SC-007).
2. Otherwise insert the event, all 120 seats with their prices, and mark the 28 seats pinned in
   [contracts/venue-seed-contract.md](./contracts/venue-seed-contract.md) as `Taken`. Everything else is
   `Available`.
3. The whole seed commits as one unit, so a start interrupted halfway cannot leave a half-built venue
   (the "store is empty but the application expects content" edge case).

The seed is a constant, not a computation: no randomness runs in production code (see R4 in
[research.md](./research.md)).

---

## Projection to the UI (`Models/`)

| UI type | Built from | Notes |
|---|---|---|
| `ConcertEvent` | `EventRecord` | `DateText`/`TimeText` keep formatting; `Date` becomes `DateTimeOffset`-backed. |
| `Seat` | `SeatRecord` + this circuit's selection | `State` = `Selected` if chosen in this circuit, otherwise `Occupied` when the stored status is `Taken`, otherwise `Available`. `Price`, `Row`, `Number`, `Label` unchanged. |
| `SeatState` | — | `Available`, `Selected`, `Occupied`. `Unavailable` removed. |
| `PurchaseReceipt` | `PurchaseRecord` + its lines | Seats (label + price paid) and the total, read back after commit. This is what the confirmation dialog renders (FR-014). |

`IsSelectable` stays `State is Available or Selected`, which is what disables a taken seat in the map
and satisfies US2 scenario 3 from the visitor's side.

Components never see `Data/` types: the projection happens inside `TicketingService`, so a component
cannot reach the store even by mistake (Constitution Principle I and II).

---

## Rules the model must uphold, and where they are proven

| Rule | Requirement | Proven by |
|---|---|---|
| A taken seat can never be bought again | FR-007 | Purchase re-checks status inside the transaction; `PurchaseStoreTests` |
| A purchase is all-or-nothing | FR-008, SC-005 | Transaction boundary; `PurchaseStoreTests` (forced mid-purchase failure) |
| The recorded amount does not change when a price later changes | FR-006 | `PricePaid` copied into the line; `PurchaseStoreTests` |
| Restart changes nothing | FR-009, FR-010, SC-001…SC-003 | `HostRestartTests` (two hosts, one file) |
| A fresh venue is identical every time | FR-012, SC-006 | Pinned seed constant; `VenueSeedTests` |
| A populated venue is never re-seeded | FR-011, SC-007 | Seed guard on the event row; `VenueSeedTests` |
| An unreadable store shows no seats | FR-015, SC-008 | `Home.razor` load guard; `HostRestartTests` (broken store) |
| The confirmation is derived from the record | FR-014 | Purchase re-read; `PurchaseStoreTests` |

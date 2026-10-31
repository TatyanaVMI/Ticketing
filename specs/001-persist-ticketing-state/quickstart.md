# Quickstart: proving that the ticking state survives a restart

A run-and-observe guide. Details of the shape live in [data-model.md](./data-model.md); the exact
fresh venue lives in [contracts/venue-seed-contract.md](./contracts/venue-seed-contract.md).

---

## 1. Prerequisites

- .NET SDK 10 (verified with 10.0.401).
- The EF Core CLI only if migrations have to be regenerated:
  `dotnet tool install --global dotnet-ef`.
- Two browser windows (one normal, one private) for the shared-venue checks.

## 2. Build and run

```powershell
dotnet restore
dotnet build
dotnet run --project Ticketing.csproj
```

The application listens on `http://localhost:5097` (see `Properties/launchSettings.json`).

The store is created on first start at `App_Data/ticketing.db`, relative to the application's content
root. It is gitignored: never commit it.

## 3. Reset to a fresh install

```powershell
Remove-Item -Recurse -Force App_Data
```

The next start builds the venue again from the seed contract.

## 4. Automated checks

```powershell
dotnet test
```

Expected: everything passes, including the restart tests. What each file proves:

| Test file | Success criteria |
|---|---|
| `tests/Ticketing.Tests/VenueSeedTests.cs` | SC-006, SC-007 — a fresh venue is identical every time; a populated one is never re-seeded |
| `tests/Ticketing.Tests/PurchaseStoreTests.cs` | SC-004, SC-005 — a taken seat is not sold twice sequentially; a failed purchase leaves nothing behind |
| `tests/Ticketing.Tests/HostRestartTests.cs` | SC-001, SC-002, SC-008, SC-009 — state survives a real host restart; an unreadable store shows no seats |
| `tests/Ticketing.Tests/SharedVenueTests.cs` | SC-003, FR-010 — two visitors who each read the venue agree on every seat |

A build with a new warning, or any failing test, means the change is not ready (Constitution Quality
Gates).

## 5. Manual validation, story by story

### User Story 1 — a purchase is still there after a restart (P1, SC-001, SC-002)

1. Open the page, choose two seats, buy them. Read the confirmation.
2. Stop the application (Ctrl+C) and start it again.
3. Confirm the same two seats are shown as taken, and that nothing else changed.
4. Confirm the confirmation you were shown matched the recorded purchase: same seats, same total.

Expected: the seats stay taken across the restart; the total never re-derives from a later price.

### User Story 2 — every visitor sees the same venue (P2, SC-003, FR-010)

1. Open the page twice, in two independent browser windows.
2. Buy a seat in the first window.
3. In the second window, **without reloading**, try to choose the seat that was just sold: you are
   refused, and the seat drops out of your selection.
4. Reload the second window: the seat is shown as taken there too, and the two windows now agree on
   every seat in the venue.

Expected: one seat sold once, one venue for everyone, and no page can ever buy a seat that is already
gone. Freshness is deliberately not pushed — see research R7 and the spec's 2026-10-31 clarifications —
which is why step 3 is a refusal rather than a silent update.

### User Story 3 — a fresh installation is a complete, correct venue (P3, SC-006, SC-007)

1. Delete `App_Data` and start the application.
2. Count the seats: 10 rows, 12 each, 120 total.
3. Check the taken seats: they must be exactly the 28 listed in the seed contract.
4. Check the legend: **three** entries — available, selected, occupied. There is no "unavailable".
5. Stop and start again, twice. Confirm the venue is not duplicated and no status has been reset.

### Failure path — the store cannot be read (FR-015, SC-008)

1. Stop the application.
2. Replace `App_Data/ticketing.db` with a file that is not a database (or make it unreadable).
3. Start the application and open the page.

Expected: a clear error is reported and **no seats are shown** — in particular the venue must never
appear as fully available. Restore or delete the store afterwards.

## 6. Two things to check by eye, because they are the only intended visible changes

- The seat map's legend no longer offers an "unavailable" state (FR-016).
- The confirmation dialog no longer tells the visitor to present anything at the door (FR-018).

Everything else about the journey — choosing seats, seeing the total, buying, reading the confirmation
— must look and behave exactly as before (SC-010).

## 7. Known gaps, so nobody mistakes them for bugs

- Two different visitors buying the same seat in the same instant may both succeed. Sequential use is
  protected; the rest is an accepted gap (spec Assumptions, SC-004).
- A page that stays open does not follow other visitors' purchases until it reads again: the seat may
  still look free, and choosing it is refused against the store. This is deliberate (spec Clarifications,
  2026-10-31), not a bug.
- The confirmation dialog is still not keyboard-trapped, cannot be closed with Escape, and does not
  restore focus; the seat map still overflows horizontally on narrow screens. Both are deferred as a
  documented exception (spec Clarifications Q2) and are the next feature, not defects introduced here.

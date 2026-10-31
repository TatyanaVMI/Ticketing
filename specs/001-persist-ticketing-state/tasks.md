# Tasks: Persist Ticketing State

**Input**: Design documents from `specs/001-persist-ticketing-state/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts/), [quickstart.md](./quickstart.md)

**Tests**: Included, and not optional here. FR-017 and SC-009 require the guarantees to be confirmable
automatically on every change, without manual interaction with the interface — so the test tasks below
are part of the feature, not an extra.

**Organization**: Tasks are grouped by user story so each story can be implemented, tested, and
delivered on its own.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3)
- Every task names the exact file it touches

## Path Conventions

Single web application at the repository root plus one test project, as fixed in
[plan.md](./plan.md) → Project Structure:

- Application: `Components/`, `Data/`, `Models/`, `Pages/`, `Services/`, `Layout/`, project root files
- Tests: `tests/Ticketing.Tests/`
- Runtime state: `App_Data/` (never committed)

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Make the project a server-side web application with a store and a test project.

- [X] T001 Change `Ticketing.csproj` to SDK `Microsoft.NET.Sdk.Web`, remove both `Microsoft.AspNetCore.Components.WebAssembly*` package references, and keep `TargetFramework` `net10.0`, `Nullable` `enable`, `ImplicitUsings` `enable` as they are (Constitution Principle VI)
- [X] T002 [P] Add package references to `Ticketing.csproj`: `Microsoft.EntityFrameworkCore.Sqlite` and `Microsoft.EntityFrameworkCore.Design` with `PrivateAssets="all"`
- [X] T003 [P] Create `tests/Ticketing.Tests/Ticketing.Tests.csproj` targeting `net10.0` with `Microsoft.NET.Test.Sdk`, `xunit`, `xunit.runner.visualstudio`, `Microsoft.AspNetCore.Mvc.Testing`, and `Microsoft.EntityFrameworkCore.Sqlite`, and a `ProjectReference` to `Ticketing.csproj`
- [X] T004 Create `Ticketing.sln` at the repository root and add both projects to it, so one command builds and tests everything (depends on T001, T003)
- [X] T005 [P] Add `App_Data/` to `.gitignore` — the store is runtime state and must never be committed
- [X] T006 [P] Add `appsettings.json` and `appsettings.Development.json` with `ConnectionStrings:Ticketing` = `Data Source=App_Data/ticketing.db`, resolved relative to the content root (research R8)

**Checkpoint**: `dotnet build` succeeds against the new SDK; the test project is discoverable.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The persistence model, the fresh venue, the state layer, and the hosting migration. No
user story can be delivered before this is complete.

**CRITICAL**: No user story work can begin until this phase is complete.

### Persistence model

- [X] T007 [P] Create `Data/SeatStatus.cs` with `Available` and `Taken` only — there is no stored "unavailable" state any more (spec FR-004)
- [X] T008 [P] Create `Data/EventRecord.cs` with `Id` (int, primary key), `Name`, `Artist`, `Venue` (required strings) and `StartsAt` (`DateTimeOffset`, required)
- [X] T009 [P] Create `Data/SeatRecord.cs` with `Id` (int, primary key), `EventId` (int, required FK → `EventRecord`, cascade delete), `Row` (required string, single letter `A`–`J`), `Number` (int, 1–12), `Price` (decimal, required), `Status` (`SeatStatus`, required)
- [X] T010 [P] Create `Data/PurchaseRecord.cs` with `Id` (int, primary key), `EventId` (int, required FK → `EventRecord`, restrict delete), `CreatedAt` (`DateTimeOffset`, required), `TotalAmount` (decimal, required)
- [X] T011 [P] Create `Data/PurchaseLineRecord.cs` with `Id` (int, primary key), `PurchaseId` (int, required FK → `PurchaseRecord`, cascade delete), `SeatId` (int, required FK → `SeatRecord`, restrict delete), `PricePaid` (decimal, required)
- [X] T012 Create `Data/TicketingDbContext.cs` with the four `DbSet`s and this model configuration: unique index on (`EventId`, `Row`, `Number`); `SeatRecord.Status` converted to **text**, not an integer; unique index on (`PurchaseId`, `SeatId`); decimal precision fixed so `PricePaid` and `TotalAmount` round-trip exactly
- [X] T013 [P] Create `Data/DesignTimeDbContextFactory.cs` implementing `IDesignTimeDbContextFactory<TicketingDbContext>` so `dotnet ef` can build a context although the application registers a factory (research R3)
- [X] T014 Generate the initial migration into `Data/Migrations/` with `dotnet ef migrations add InitialCreate`

### Presentation types

- [X] T015 [P] Edit `Models/SeatState.cs` to remove the `Unavailable` member, leaving `Available`, `Selected`, `Occupied`; `Occupied` stays because it is the word the legend shows (FR-016, Constitution Principle VI)
- [X] T016 [P] Create `Models/PurchaseReceipt.cs` holding the seats bought (label + price paid) and the total amount, as read back from the committed record

### Components that must move with the model

- [X] T017 [P] Edit `Components/SeatButton.razor` to drop both `SeatState.Unavailable` branches from `StateClass`, `AriaLabel`, and `Title` (must land together with T015 — same enum, different files)
- [X] T018 [P] Edit `Components/SeatMap.razor` to remove the "Unavailable" legend entry, leaving exactly three: available, selected, occupied (FR-016)

### The fresh venue

- [X] T019 Create `Data/TicketingSeeder.cs` inserting the event (Name `Midnight Frequencies Tour`, Artist `The Static Fields`, starts 2026-11-21 20:00 with its offset, Venue `Grand Hall Auditorium`), all 120 seats with prices by row (`A` 150; `B`–`D` 120; `E`–`H` 90; `I`–`J` 60), and marking exactly the 28 seats listed in [contracts/venue-seed-contract.md](./contracts/venue-seed-contract.md) as `Taken` — every other seat `Available`. Guard: if an event row already exists, change nothing. The whole seed commits as one transaction. No `Random` anywhere.

### State layer

- [X] T020 Rewrite `Services/TicketingService.cs` as a scoped service over `IDbContextFactory<TicketingDbContext>`: `LoadAsync`, `IsLoaded`, `LoadError`, `Event`, `Rows`, `SelectedSeats`, `SelectedCount`, `TotalPrice`, `CanBuy`, `OnChange`, `Task ToggleSeat(Seat)`, `Task<PurchaseOutcome> PurchaseAsync()`. Selection is circuit-local and never written to the store. `PurchaseAsync` opens one explicit transaction, re-reads the selected seats, refuses if any is `Taken`, writes the purchase and its lines with `PricePaid` copied from the stored price, marks the seats `Taken`, commits, and returns the new purchase's id. `OnChange` is raised only inside this circuit; no visitor-to-visitor notification exists (contract §5). Contract: [contracts/service-contract.md](./contracts/service-contract.md) §2–§4.

### Hosting migration

- [X] T021 Migrate the host: rewrite `Program.cs` as an ASP.NET Core host (register the context factory, the seeder on start, `TicketingService`, Razor components with interactive server rendering; end the file with `public partial class Program { }` for the tests); turn `App.razor` into the root HTML document; add `Routes.razor` holding the existing `<Router>` with `NotFoundPage="typeof(Pages.NotFound)"`; update `_Imports.razor` to server usings; remove `inspectUri` from `Properties/launchSettings.json`; delete `wwwroot/index.html` (research R1)
- [X] T022 Edit `Pages/Home.razor` to load through the service, render an error panel instead of the workspace when `LoadError` is set (never the seat map, never the buy action — FR-015, SC-008), and keep the current journey otherwise: same components, same order, same steps

**Checkpoint**: the application starts against an empty store, builds the venue, sells seats, and the
confirmation still behaves exactly as it did before the migration.

---

## Phase 3: User Story 1 - A purchase is still there after a restart (Priority: P1) — MVP

**Goal**: buy seats, restart the application, and find the venue and the purchase unchanged.

**Independent Test**: buy at least two seats, dispose the host, start a second host against the same
store, and assert that the seats are still taken and the purchase still retrievable with the same seats
and the same amount.

### Tests for User Story 1

- [X] T023 [P] [US1] Create `tests/Ticketing.Tests/PurchaseStoreTests.cs`: a committed purchase writes the record, one line per seat with `PricePaid` equal to the stored price, and the seat statuses, all together (FR-005, FR-008, SC-005); a later change to `SeatRecord.Price` leaves `TotalAmount` and `PricePaid` untouched (FR-006); a seat recorded as `Taken` cannot be bought again sequentially (FR-007, SC-004); a forced failure mid-purchase leaves zero purchases and zero taken seats (SC-005); the receipt re-read after commit matches the purchase (FR-014)
- [X] T024 [US1] Create `tests/Ticketing.Tests/HostRestartTests.cs` using `WebApplicationFactory<Program>` over one temporary SQLite file: start a host, buy two seats through the service, dispose the host, start a second host over the same file, and assert 100% of the previously taken seats are still taken (SC-001), the purchase is retrievable with the same seats and amount (SC-002, FR-009), and the served page no longer offers the bought seat

### Implementation for User Story 1

- [X] T025 [P] [US1] Edit `Components/PurchaseDialog.razor` to render from `PurchaseReceipt` and to stop instructing the visitor to present the confirmation at the door — it must claim only the seats bought, the amount paid, and that the purchase is complete (FR-018)
- [X] T026 [US1] Edit `Pages/Home.razor` to replace the in-page `purchasedSeats`/`purchasedTotal` snapshot: on `PurchaseOutcome.Succeeded`, re-read the purchase from the store and feed the dialog from that record; on `SeatNoLongerAvailable`, drop that seat from the selection and tell the visitor without adding or removing a journey step (FR-014, SC-010, Constitution Principle II)

**Checkpoint**: User Story 1 is fully functional and verifiable on its own — this is the MVP.

---

## Phase 4: User Story 2 - Every visitor sees the same venue (Priority: P2)

**Goal**: no visitor can ever act on a stale picture of the venue — a seat that is taken in the store is
taken for everyone, and a page that has not re-read can never buy it.

**Independent Test**: two independent service instances over one store; buy in the first; the second,
re-read, agrees on every seat; and choosing a seat that is taken in the store is refused even while the
second instance's local view still shows it free.

### Tests for User Story 2

- [X] T027 [P] [US2] Create `tests/Ticketing.Tests/SharedVenueTests.cs`: two service instances over one store report identical availability for the whole inventory once each has read the venue after a purchase in one of them (FR-010, SC-003); a seat whose stored status is `Taken` cannot be added to the selection even when the local view still shows it free (US2 scenario 3, FR-007); a failed purchase changes no seat anywhere

### Implementation for User Story 2

- [X] T028 [US2] Edit `Services/TicketingService.cs` so `ToggleSeat` confirms against the store that the seat is not `Taken` before accepting it, and drops it from the selection if it was there — the local view is never trusted over the store (contract §3 and §5). No notification mechanism is added: `OnChange` stays inside its own circuit

**Checkpoint**: User Stories 1 and 2 both work, independently testable.

---

## Phase 5: User Story 3 - A fresh installation is a complete, correct venue (Priority: P3)

**Goal**: a first start produces the complete venue, and a later start leaves it alone.

**Independent Test**: start against an empty store and count what appears; start twice more and assert
nothing was duplicated or reset.

### Tests for User Story 3

- [X] T029 [P] [US3] Create `tests/Ticketing.Tests/VenueSeedTests.cs` asserting a fresh store yields exactly 120 seats in 10 rows of 12, each seat priced by its row, and exactly the 28 pinned taken seats from [contracts/venue-seed-contract.md](./contracts/venue-seed-contract.md) — no more, no fewer, none other; and that two fresh runs produce the identical venue (FR-012, SC-006)
- [X] T030 [US3] Extend `tests/Ticketing.Tests/VenueSeedTests.cs` to start a second time against the populated store and assert zero duplicate seats, zero reset statuses, and that a seat bought in between is still bought (FR-011, SC-007)

### Implementation for User Story 3

- [X] T031 [US3] Harden `Data/TicketingSeeder.cs`: make the guard explicit (an existing event row means "do nothing at all"), keep the whole seed inside one transaction so an interrupted start cannot leave a half-built venue, and confirm nothing in it depends on time, randomness, or the machine (FR-011, FR-012)

**Checkpoint**: all three user stories are independently functional.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [X] T032 Sweep for WebAssembly leftovers now that the host changed: stale usings in `_Imports.razor`, `wwwroot/index.html`, the `blazor.webassembly.js` reference, `OverrideHtmlAssetPlaceholders`, and `launchSettings.json` entries that no longer apply (Constitution Principle V: dead code is removed, not left dormant)
- [X] T033 Build clean: run `dotnet build` and fix every warning — warnings are defects under Constitution Principle VI and Quality Gate 1
- [X] T034 Run `dotnet test` and confirm every test mapped to SC-001…SC-009 passes (Quality Gate 2 and 5)
- [X] T035 Walk through [quickstart.md](./quickstart.md) by hand: both visible changes by eye (three-entry legend, no "present at the door"), the stale-page refusal, the failure path against an unreadable store, and a reset to a fresh install (FR-016, FR-018, SC-008, SC-010)
- [X] T036 Confirm the store is untracked: `git status` shows no file under `App_Data/`, and no `.db` file is staged (constitution Technology & Data Constraints — the store is never committed)
- [X] T037 Commit as one coherent change with what changed, why, and how it was verified — including the explicit statement that the accessibility exception (spec Clarifications Q2, plan Complexity Tracking) is deliberate and tracked as separate work (Constitution Quality Gate 4)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies
- **Foundational (Phase 2)**: depends on Setup; blocks every user story
- **User Stories (Phases 3–5)**: each depends only on Foundational, so all three may proceed in parallel; sequentially they run P1 → P2 → P3
- **Polish (Phase 6)**: depends on the stories you intend to ship

### Within phases

- T004 depends on T001 and T003
- T012 depends on T007–T011; T014 depends on T012 and T013
- T017 must land with T015, or the build breaks
- T020 depends on T012 and T019; T021 and T022 depend on T020
- T026 depends on T016 and T025
- T028 edits the same file T020 rewrote, so it comes after it
- T030 extends the file T029 creates; T031 edits the seeder both read

### Independent test criteria

| Story | Verified by | Passes when |
|---|---|---|
| US1 (P1) | T023, T024 | After a real host restart the seats are still taken and the purchase still returns the same seats and amount |
| US2 (P2) | T027 | Two instances that have each read the venue agree on every seat, and a seat taken in the store cannot be chosen locally |
| US3 (P3) | T029, T030 | A fresh store yields exactly the contract's venue; a second start changes nothing |

---

## Parallel Example: User Story 1

```bash
# Both test files are independent:
Task: "Create tests/Ticketing.Tests/PurchaseStoreTests.cs"        # T023
Task: "Create tests/Ticketing.Tests/HostRestartTests.cs"          # T024

# The dialog and the page are different files, but T026 depends on T025:
Task: "Edit Components/PurchaseDialog.razor"                      # T025
Task: "Edit Pages/Home.razor"                                     # T026 (after T025)
```

## Parallel Example: Foundational phase

```bash
# The four records have no relationship at build time:
Task: "Create Data/EventRecord.cs"          # T008
Task: "Create Data/SeatRecord.cs"           # T009
Task: "Create Data/PurchaseRecord.cs"       # T010
Task: "Create Data/PurchaseLineRecord.cs"   # T011

# The presentation types and the components that must move with them:
Task: "Edit Models/SeatState.cs"            # T015
Task: "Create Models/PurchaseReceipt.cs"    # T016
Task: "Edit Components/SeatButton.razor"    # T017
Task: "Edit Components/SeatMap.razor"       # T018
```

---

## Implementation Strategy

### MVP first (User Story 1 only)

1. Phase 1 — Setup
2. Phase 2 — Foundational (blocks everything)
3. Phase 3 — User Story 1
4. Stop and validate: buy seats, restart, check
5. That is a shippable demo: the application survives a restart

### Incremental delivery

1. Setup + Foundational → the app runs against a store (no new guarantees yet)
2. US1 → restart survival, verified by tests and by hand (MVP)
3. US2 → no visitor can act on a stale picture; a sold seat is refused everywhere
4. US3 → a fresh install is provably correct
5. Polish → clean build, full walkthrough, one commit

### Parallel team strategy

After Foundational, three people can take US1, US2, and US3 with no file overlap: US1 works in
`tests/PurchaseStoreTests.cs`, `tests/HostRestartTests.cs`, `Components/PurchaseDialog.razor`, and
`Pages/Home.razor`; US2 works in `Services/TicketingService.cs` and `tests/SharedVenueTests.cs`; US3
works in `Data/TicketingSeeder.cs` and `tests/VenueSeedTests.cs`.

---

## Notes

- [P] tasks touch different files and have no unfinished dependencies
- Every task names its file; nothing is left to implementation-time discretion
- Tests are part of the feature because FR-017 and SC-009 demand automatic confirmation
- Verify a test fails before making it pass; a test that cannot fail proves nothing
- Commit after each logical group; the working agreement is one coherent change per pull request
- Deliberately not in scope, so nobody adds them by reflex: concurrency protection (spec Assumptions),
  accessibility remediation (spec Clarifications 2026-10-25, Q2), any purchase code or "my tickets"
  lookup (FR-018), and any live push between visitors (spec Clarifications 2026-10-31)

---

## Phase 7: Convergence

- [X] T038 Snapshot the whole stored state in `tests/Ticketing.Tests/HostRestartTests.cs` before the host is disposed — the event (`Name`, `Artist`, `StartsAt`, `Venue`), all 120 seats (`Row`, `Number`, `Price`, `Status`), and every purchase with its `CreatedAt`, `TotalAmount`, and lines — then rebuild the host over the same file and assert every captured value is identical, and assert the served page still renders the event and the bought seat as occupied — per FR-001, FR-002, FR-003, FR-005, FR-009, FR-017, SC-009 (partial)
- [X] T039 Extend `tests/Ticketing.Tests/VenueSeedTests.cs` `Starting_again_over_a_populated_store_changes_nothing` to assert the purchase row, its lines, `TotalAmount`, and `CreatedAt` still exist unchanged after two further `SeedAsync` runs — per FR-011, SC-007 (partial)
- [X] T040 Add a repository-level CI workflow (for example `.github/workflows/ci.yml`) that runs `dotnet restore`, `dotnet build --no-restore`, and `dotnet test --no-build` for `Ticketing.sln` on every push and pull request, so the guarantees are confirmed automatically whenever the project changes — per FR-017, SC-009 (missing)
- [X] T041 Remove the CSS rules in `wwwroot/css/app.css` that no rendered markup uses after `wwwroot/index.html` was deleted — `.loading-progress`, `.loading-progress circle`, `.loading-progress-text`, and `.blazor-error-boundary` — while keeping `#blazor-error-ui`, which `App.razor` renders — per T032, Constitution V (partial)
- [X] T042 Filter or order the event lookup in `Services/TicketingService.LoadAsync` (for example `OrderBy(eventRecord => eventRecord.Id)` or an explicit single-row predicate) so EF Core no longer logs `Query[10103]` on every load and the event choice is deterministic — per Constitution VI, T033 (partial)

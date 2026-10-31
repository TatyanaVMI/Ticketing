# Implementation Plan: Persist Ticketing State

**Branch**: `001-persist-ticketing-state` | **Date**: 2026-10-25 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/001-persist-ticketing-state/spec.md`

## Summary

The venue, its seat statuses, prices, and every completed purchase must live in a durable store that
every visitor reads, so that a restart erases nothing. Today the application is a client-only Blazor
WebAssembly app whose whole venue is generated inside each browser session.

The technical approach: move the authoritative state into SQLite through Entity Framework Core, host
the same UI as a Blazor Web App rendered on the server (InteractiveServer), and turn `TicketingService`
from the owner of the venue into the single access layer over the store. Components, layout, and the
visitor's journey stay as they are; the only visible changes are the two the spec names in FR-016
(the unavailable state disappears, and the confirmation stops promising something to present at the
door).

## Technical Context

**Language/Version**: C# 14 on `net10.0` (SDK 10.0.401 verified on the development machine)

**Primary Dependencies**: Blazor Web App (`Microsoft.AspNetCore.Components.Web`), `Microsoft.EntityFrameworkCore.Sqlite`, `Microsoft.EntityFrameworkCore.Design` (design-time only, for migrations); `Microsoft.NET.Test.Sdk` + `xunit` + `Microsoft.AspNetCore.Mvc.Testing` in the test project. No new UI, state-management, or mapping libraries (constitution Principle V).

**Storage**: SQLite, one file per deployment, accessed only through EF Core. Schema created by migrations, applied on start. Path comes from configuration (`App_Data/ticketing.db` by default) and is not committed.

**Testing**: `dotnet test` with xUnit, in two levels — store-level tests against a real temporary SQLite file, and host-level restart tests that build the application, buy seats, dispose the host, rebuild it against the same file, and assert the state survived.

**Target Platform**: Cross-platform server process (ASP.NET Core / Kestrel, InteractiveServer), developed on Windows.

**Project Type**: Single web application plus one test project.

**Performance Goals**: No numeric target is set; the spec deliberately leaves the constitution's qualitative bar in place ("reads fast enough to keep the seat map usable"). Practically: the venue is one small in-process SQLite read per circuit, and no interaction may require a full page reload.

**Constraints**: One instance, one store, one event. Protection against two simultaneous purchases of the same seat is sequential only — an accepted, documented gap. No authentication, no payment, synthetic data only. Interactive UI must keep its current behaviour (FR-016) and its known accessibility gaps stay out of scope (documented exception).

**Scale/Scope**: 1 event, 120 seats (10 rows × 12), 4 price tiers, 3 user stories, 18 functional requirements, 10 success criteria. Purchase history is unbounded but small; concurrency is measured in a handful of visitors, not thousands.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| # | Gate | Verdict | Basis |
|---|------|---------|-------|
| I | Component-First Architecture — components exchange data through parameters and events, never through each other's internals | PASS | Components keep their current public shape. The store is reached only by `TicketingService`; persistence records live in `Data/` and are projected to `Models/` before any component sees them, so a component cannot reach the store even by accident. |
| II | Single Source of Truth for State — persistent store, single access layer, atomic purchase, no divergent copies | PASS | The store becomes the only authority. `TicketingService` (scoped per circuit) owns access and keeps only the visitor's unsaved selection. `Pages/Home.razor` loses its `purchasedSeats`/`purchasedTotal` snapshot: the confirmation is re-read from the committed record (FR-014). |
| III | Testable, Deterministic Behavior — deterministic seed, automated tests for transitions and restart survival | PASS | The fresh venue is built from an explicit, pinned inventory (no randomness at runtime), so it is reproducible by construction. Automated tests cover seeding, seat-status transitions, atomic purchase, and restart survival (FR-017, SC-009). |
| IV | Accessibility and Responsive UX — focusable controls, dialogs trap focus and restore it, usable from mobile widths | **EXCEPTION** | The touched UI (confirmation dialog, seat map) has pre-existing gaps that this feature neither introduces nor closes. Deferred by an explicit decision recorded in `spec.md` (Clarifications, Q2) and justified below under Complexity Tracking. |
| V | Simplicity Over Speculation — no package, layer, or hook without a concrete need | PASS | Only the store package, its migrations tooling, and test packages are added; no repository layer, no mapping library, no background service, and no notification mechanism between visitors — freshness comes from reading the store, which is required anyway. |
| VI | Platform and Language Conventions — `Nullable`/`ImplicitUsings` authoritative, warnings are defects, names consistent with existing vocabulary | PASS | The project's declared settings are kept. Public names stay in the existing vocabulary (`Seat`, `SelectedSeats`, `TotalPrice`, `CanBuy`, `SeatState.Occupied`); the spec's plain-language "taken" maps to `SeatState.Occupied`, so the legend wording does not change. |
| T&D | Technology & Data Constraints — Blazor Web App on `net10.0` with InteractiveServer; EF Core + SQLite; no external API; persistence required | PASS | This is exactly the stack; no external service or second deployable is introduced. |
| QG | Quality Gates — zero errors and no new warnings, domain tests pass, accessibility expectations met for touched UI, no undocumented violation, simplest solution | PASS with one documented exception | The accessibility expectation is the exception above (Quality Gate 4 requires it to be recorded, which it is). |

## Project Structure

### Documentation (this feature)

```text
specs/001-persist-ticketing-state/
├── plan.md              # This file
├── research.md          # Phase 0 output — decisions and alternatives
├── data-model.md        # Phase 1 output — entities, rules, transitions
├── quickstart.md        # Phase 1 output — how to run and validate
├── contracts/
│   ├── service-contract.md      # What the UI may ask of the state layer
│   └── venue-seed-contract.md   # The exact fresh venue a first start must produce
├── checklists/
│   └── requirements.md  # Spec quality checklist (created by /speckit.specify)
└── tasks.md             # Phase 2 output (/speckit.tasks — not created here)
```

### Source Code (repository root)

```text
Ticketing.sln                          # new: one command builds and tests everything

Ticketing.csproj                       # SDK changes to Microsoft.NET.Sdk.Web
Program.cs                             # host, DI, migrate + seed on start, public Program for tests
App.razor                              # becomes the root HTML document (replaces wwwroot/index.html)
Routes.razor                           # new: the router, marked InteractiveServer
_Imports.razor                         # server usings instead of WebAssembly ones
appsettings.json                       # connection string
appsettings.Development.json
Properties/launchSettings.json         # inspectUri removed (WebAssembly-only setting)

Components/                            # public shape unchanged
├── EventHeader.razor
├── SeatMap.razor                      # legend loses the "Unavailable" entry
├── SeatButton.razor                   # unavailable branches removed
├── SelectionSummary.razor
├── PurchaseDialog.razor               # wording per FR-018
└── *.razor.css

Layout/MainLayout.razor
Pages/
├── Home.razor                         # load guard + error state; confirmation from the store
└── NotFound.razor

Models/                                # UI-facing types; free of persistence concerns
├── ConcertEvent.cs
├── Seat.cs
├── SeatState.cs                       # Available, Selected, Occupied ("Unavailable" removed)
└── PurchaseReceipt.cs                 # new: projection of a committed purchase

Services/
└── TicketingService.cs                # scoped per circuit: selection + sole store access

Data/                                  # persistence; never referenced by components
├── TicketingDbContext.cs
├── EventRecord.cs
├── SeatRecord.cs
├── SeatStatus.cs
├── PurchaseRecord.cs
├── PurchaseLineRecord.cs
├── TicketingSeeder.cs
├── DesignTimeDbContextFactory.cs
└── Migrations/                        # generated

tests/Ticketing.Tests/
├── Ticketing.Tests.csproj
├── VenueSeedTests.cs                  # FR-011, FR-012, SC-006
├── PurchaseStoreTests.cs              # FR-005…FR-008, SC-004, SC-005, SC-007
└── HostRestartTests.cs                # FR-009, FR-010, FR-017, SC-001…SC-003, SC-009

wwwroot/
├── css/app.css
├── favicon.png
└── index.html                         # deleted

App_Data/ticketing.db                  # runtime store; gitignored
```

**Structure Decision**: The application stays a single project at the repository root — the existing
layout (`Components/`, `Pages/`, `Models/`, `Services/`, `Layout/`) is preserved and one new folder,
`Data/`, holds the persistence records and the context. Introducing a second web project or a separate
domain assembly would split a 120-seat venue across three build units for no current requirement
(Principle V). The test project is the only addition, because FR-017 and SC-009 require automated
confirmation, and it lives under `tests/` so it is never compiled into the deployed app.

## Complexity Tracking

> Filled because Constitution Check has one violation that must be justified.

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|--------------------------------------|
| Principle IV (Accessibility) is not satisfied for the UI this feature touches — the confirmation dialog still does not trap focus, cannot be dismissed from the keyboard, and does not restore focus; the seat map still overflows horizontally at narrow widths | This feature moves state, not interaction. Closing those gaps means designing and validating keyboard behaviour, which is a separate requirement with its own acceptance criteria; bundling it would make a storage migration and an interaction redesign land in one change, contrary to the constitution's workflow rule of one coherent change per pull request | Doing the work here was considered and rejected as scope mixing, not as unnecessary work: it is genuinely needed, and it must be tracked as the next feature. Until then Quality Gate 4 is satisfied only because the exception is written down in `spec.md` (Clarifications Q2, Assumptions) and here — the two reviews that would otherwise fail silently are the constitution check in this plan and the merge gate |

**Also recorded, though not a constitution violation**: this iteration offers no protection against two
visitors buying the same seat in the same instant. The spec accepts that gap explicitly (Assumptions,
SC-004 is scoped to sequential use); the plan's job is only to make sure the gap is not widened — a
purchase still commits as one unit, so the failure mode is a double sale, never a half-written record.

## Phase 0 and Phase 1 Outputs

- [research.md](./research.md) — every technical unknown resolved to a decision with alternatives.
- [data-model.md](./data-model.md) — entities, constraints, state transitions, and the projection from records to the UI.
- [contracts/service-contract.md](./contracts/service-contract.md) — what the UI may ask of the state layer, and what it must not do.
- [contracts/venue-seed-contract.md](./contracts/venue-seed-contract.md) — the exact fresh venue a first start must produce.
- [quickstart.md](./quickstart.md) — how to run it and how to validate each user story end to end.

## Post-Design Constitution Check

Re-evaluated after Phase 1. The design introduces no new dependency, layer, or store, and no new
violation:

| Gate | Verdict after design | What the design added |
|------|----------------------|------------------------|
| I | PASS | `Data/` records are projected to `Models/` before components see them; components keep their current parameters. |
| II | PASS | One access layer (`TicketingService`); the confirmation is re-read from the committed record. |
| III | PASS | The pinned seed contract removes randomness; tests are named per requirement in the structure above. |
| IV | EXCEPTION (unchanged) | Nothing in Phase 1 widens the exception; no new interactive control is introduced. |
| V | PASS | No new runtime mechanism beyond the store itself; everything reuses existing components and the platform. |
| VI | PASS | Enum and public names unchanged apart from removing `Unavailable`. |

<!--
Sync Impact Report
- Version change: 1.0.0 → 2.0.0 (MAJOR)
- Modified principles: II (Single Source of Truth for State); III (Testable, Deterministic
  Behavior — scope of mandatory tests extended)
- Added sections: none
- Removed sections: none
- Modified sections: Technology & Data Constraints (stack, state, performance)

Amendment 2.0.0 — written proposal
  Rationale: ticket purchases and seat availability MUST survive a restart of the application and
  MUST be the same for every visitor. The 1.0.0 constraints ("In-memory only"; "There is no backend,
  database, or external API"; "client-only experience that runs without a server round trip") make
  that outcome impossible: state lives inside one browser and is generated per visitor, so a seat
  taken by one visitor stays on sale for the next, and nothing outlives a reload.
  Change: authoritative state moves into a persistent store, accessed by a single server-side layer
  and hosted by a Blazor Web App on `net10.0` using InteractiveServer rendering. The client-only and
  in-memory-only obligations are withdrawn. The whole domain state — event, seat inventory, prices,
  seat statuses, and orders — MUST be persisted, not only purchases.

Amendment 2.0.0 — impact assessment
  - Specs: any spec that assumed a client-only, in-memory application is invalidated. In particular
    the assumption "No backend, storage, or external service is introduced — as required by the
    project constitution" MUST NOT be carried into new work.
  - Plans/tasks: the hosting model, project SDK, application host, and ownership of state all change;
    plans MUST NOT assume a browser-only runtime or client-side authority over seat state.
  - Code: `Services/TicketingService.cs` ceases to be the authority. Its hard-coded event,
    procedurally generated seat inventory, and in-code price tiers become initial seed data;
    `Purchase()` — currently a state flag flip — becomes a persisted order.
  - Process: MAJOR is required because an existing obligation is withdrawn rather than clarified.
    The amendment MUST be in place before the persistence work is planned.

- Templates requiring updates:
  - .specify/templates/plan-template.md (verify the "Constitution Check" gates reference this file;
    plans MUST verify the persistent-store and server-side-authority obligations)
  - .specify/templates/spec-template.md (no change required)
  - .specify/templates/tasks-template.md (verify test tasks cover order creation and persistence)
- Follow-up TODOs:
  - Automated tests for the domain remain absent; Principle III is unsatisfied today and the
    persistence feature is expected to close that gap.
  - Protection against two concurrent purchases of the same seat is not specified yet. It MUST remain
    an explicit, documented gap in the relevant spec rather than an accidental one.
-->

# Ticketing Constitution

## Core Principles

### I. Component-First Architecture

Every user-facing capability MUST be delivered as a Razor component with a single,
clearly stated responsibility. Components MUST NOT reach into each other's internals;
they exchange data through parameters and events only.

- Feature code lives under `Components/` (reusable UI), `Pages/` (routed entry points),
  `Models/` (domain types), and `Services/` (state and behavior).
- A component that grows two unrelated responsibilities MUST be split before further work continues.
- Styling MUST stay scoped (`.razor.css`) unless it is genuinely app-wide, in which case it belongs in `wwwroot/css/app.css`.
- Rationale: predictable ownership keeps the seat map, dialogs, and summaries independently reviewable.

### II. Single Source of Truth for State

Authoritative application state MUST live in the persistent store, accessed through a single
server-side layer. `TicketingService` (or its successor) owns *access* to that state; components
MUST treat it as the only authority and MUST NOT keep divergent copies, including totals and
receipts.

- State changes MUST flow through explicit service methods (selecting a seat, purchasing), never
  through data access performed directly by a component.
- A purchase MUST commit as one unit: it either records the order and takes the seats, or it changes
  nothing at all.
- Domain values MUST be exposed to components as read-only; components MUST NOT carry authoritative
  copies of seat state, prices, or totals between renders.
- The confirmation shown to a visitor MUST be derived from the persisted record, not from a
  client-side snapshot of the selection.
- Rationale: seat availability, totals, and purchase correctness are only meaningful if every
  visitor reads the same state and that state outlives a restart.

### III. Testable, Deterministic Behavior

Domain logic MUST be deterministic and testable in isolation from the UI. Any logic with a
branch, a rule, or a price calculation belongs in `Models` or `Services`, not inline in markup.

- Random or time-dependent behavior MUST be seeded or injected to remain reproducible
  (e.g. `new Random(20261121)`), so results can be asserted; initial data MUST be seeded
  deterministically so a fresh store is reproducible.
- Non-trivial seat-state transitions (available → taken) and order creation MUST have automated
  tests, including that a seat recorded as taken cannot be purchased again and that state survives
  a restart.
- Pure model types (`Seat`, `ConcertEvent`, `SeatState`) MUST remain free of UI dependencies.
- Rationale: a booking rule that cannot be tested cannot be trusted.

### IV. Accessibility and Responsive UX

Interactive UI MUST be usable with keyboard and assistive technology and MUST adapt to small screens.

- Every interactive control MUST be a real focusable element with an accessible name and state
  (a seat button must convey selected/occupied/unavailable, not color alone).
- Dialogs MUST trap focus, be dismissible via keyboard, and restore focus on close.
- Layouts MUST remain usable from mobile widths upward.
- Rationale: tickets are bought on phones, and availability must never depend on color perception.

### V. Simplicity Over Speculation

Features MUST be the simplest thing that satisfies the current requirement. Abstraction,
configuration, and dependencies are added only when an existing, concrete need demands them.

- No new package (including UI frameworks and state libraries) without a recorded justification
  and a review of what the built-in platform already provides.
- No speculative layers for work that is not yet specified (multi-event support, payments, seat
  holds); the persistence required by the Technology & Data Constraints is specified and expected.
- Dead code, unused parameters, and premature hooks MUST be removed, not left dormant.
- Rationale: a small surface area is easier to change when real requirements arrive.

### VI. Platform and Language Conventions

Code MUST follow current .NET and Blazor conventions and the settings already declared by the project.

- Target framework, `Nullable enable`, and `ImplicitUsings enable` are authoritative; new code MUST
  compile cleanly under them and MUST NOT weaken these settings.
- Nullable warnings and compiler warnings are treated as defects, not noise.
- Public API of models and services MUST use clear, intention-revealing names consistent with the
  existing vocabulary (`Seat`, `Row`, `SelectedSeats`, `TotalPrice`, `CanBuy`).
- Rationale: consistency is what lets any contributor read any file without re-learning the project.

## Technology & Data Constraints

- **Stack**: Blazor Web App on `net10.0`, rendered interactively on the server (InteractiveServer).
  Permitted dependencies are the official `Microsoft.AspNetCore.*` component packages, Entity
  Framework Core, and the SQLite provider; anything further requires an amendment.
- **State**: Persistence is required. The authoritative state — event, seat inventory, prices, seat
  statuses, and orders — MUST be stored in a database and MUST survive a restart of the application.
  There is no external API: application and data access run in one process. Durable behaviour needs
  no amendment; a *different* store, an external service, or a second deployable does.
- **Data**: No real personal data, payment data, or secrets may enter the repository, fixtures, or
  the store. Sample data MUST be clearly synthetic.
- **Performance**: Interaction MUST stay responsive — reads fast enough to keep the seat map usable —
  and no interaction may require a full page reload. A server round trip is permitted and expected.

## Development Workflow

- Work follows the spec-driven flow: **specify → plan → tasks → implement**; plans and tasks MUST be
  traceable to requirements rather than invented on the fly.
- Changes MUST be scoped: one coherent change per pull request, with a description of what changed,
  why, and how it was verified.
- New dependencies, breaking changes to shared components, or deviations from these principles MUST be
  called out explicitly in the change description and justified against this constitution.
- User-visible behavior changes MUST update or add the corresponding spec/acceptance criteria in the
  same change.

## Quality Gates

A change is ready to merge only when all of the following hold:

1. It builds with zero errors and no new warnings under the project's declared settings.
2. Automated tests for affected domain rules and state transitions pass.
3. Accessibility expectations for any touched interactive UI are met (Principle IV).
4. No principle in this constitution is knowingly violated; any justified exception is documented.
5. The change is the simplest solution to the stated requirement (Principle V).

## Governance

- This constitution supersedes conflicting local habits and ad-hoc conventions. Where it and a
  convenience conflict, the constitution wins unless formally amended.
- **Amendments** require: a written proposal describing the change and its rationale, an explicit
  impact assessment on existing specs, plans, and code, and approval by the project maintainers.
- **Versioning** follows semantic rules: MAJOR for backward-incompatible governance or principle
  changes, MINOR for a new principle or materially expanded guidance, PATCH for clarifications and
  wording that do not change obligations.
- **Compliance** is reviewed at every plan and pull request. Reviewers MUST verify that the plan's
  Constitution Check is satisfied and MUST reject changes that violate a principle without a
  documented, approved exception.
- **Runtime guidance**: this document is the authority for day-to-day decisions; when guidance
  elsewhere conflicts, treat it as a bug to be fixed here.

**Version**: 2.0.0 | **Ratified**: 2026-10-20 | **Last Amended**: 2026-10-25

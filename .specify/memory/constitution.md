<!--
Sync Impact Report
- Version change: (none) → 1.0.0
- Modified principles: n/a (initial ratification)
- Added sections: Core Principles, Technology & Data Constraints, Development Workflow, Quality Gates, Governance
- Removed sections: none
- Templates requiring updates:
  - .specify/templates/plan-template.md (verify "Constitution Check" gates reference this file)
  - .specify/templates/spec-template.md (verify mandatory UX/Accessibility section)
  - .specify/templates/tasks-template.md (verify test tasks for state transitions)
- Follow-up TODOs: none
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

Mutable application state MUST live in exactly one owning service. `TicketingService`
owns the event and seat state; components MUST treat it as the only authority and MUST NOT
keep divergent copies.

- State changes MUST flow through explicit service methods (e.g. `ToggleSeat`, `Purchase`).
- Domain values MUST be exposed as read-only (`IReadOnlyList<T>`, init-only, computed properties).
- UI refresh MUST be driven by the service's change notification, never by ad-hoc re-renders.
- Rationale: seat selection, totals, and purchase correctness all depend on one consistent state.

### III. Testable, Deterministic Behavior

Domain logic MUST be deterministic and testable in isolation from the UI. Any logic with a
branch, a rule, or a price calculation belongs in `Models` or `Services`, not inline in markup.

- Random or time-dependent behavior MUST be seeded or injected to remain reproducible
  (e.g. `new Random(20261121)`), so results can be asserted.
- Non-trivial seat-state transitions (available → selected → occupied) MUST have automated tests.
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
- No speculative layers for future backends, multi-event support, or persistence until that work
  is actually specified.
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

- **Stack**: Blazor WebAssembly on `net10.0`; the only permitted dependencies are the official
  `Microsoft.AspNetCore.Components.WebAssembly*` packages unless an amendment approves more.
- **State**: In-memory only for now. There is no backend, database, or external API; introducing any
  of these is a constitutional change and MUST go through the Governance process.
- **Data**: No real personal data, payment data, or secrets may enter the repository, fixtures, or
  client code. Sample data MUST be clearly synthetic.
- **Performance**: The application MUST remain a client-only experience that runs without a server
  round trip for interaction, keeping the initial load small and the seat map responsive.

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

**Version**: 1.0.0 | **Ratified**: 2026-10-20 | **Last Amended**: 2026-10-20

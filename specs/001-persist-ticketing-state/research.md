# Phase 0 Research: Persist Ticketing State

Every unknown in the plan's Technical Context, resolved to a decision. Each entry states what was
chosen, why, and what else was considered.

---

## R1. Hosting model: how the client-only app becomes a server app

**Decision**: Migrate `Ticketing.csproj` to `Microsoft.NET.Sdk.Web` and host the same components as a
Blazor Web App with InteractiveServer rendering. Concretely:

- `wwwroot/index.html` is deleted; `App.razor` becomes the root HTML document (`<html>`…`<body>` with
  `<HeadOutlet/>` and a `<Routes/>` component).
- A new `Routes.razor` holds the existing `<Router>` (including `NotFoundPage="typeof(Pages.NotFound)"`)
  and is the place the interactivity mode is declared.
- `Program.cs` becomes a normal ASP.NET Core host: services, `MapRazorComponents<App>().AddInteractiveServerRenderMode()`.
- `_Imports.razor` drops `Microsoft.AspNetCore.Components.WebAssembly.Http`.
- `Properties/launchSettings.json` drops `inspectUri`, which is a WebAssembly-only setting.

**Rationale**: The constitution (Technology & Data Constraints, version 2.0.0) mandates this hosting
model, and it is the only one of the candidates where a single process both renders the UI and owns
the authoritative state — which is what makes "every visitor sees the same venue" and "nothing is lost
on restart" achievable without inventing an API between two deployables.

**Alternatives considered**:

- *Keep WebAssembly and add a backend API* — rejected: the constitution forbids a second deployable
  for this iteration, and an API layer would need its own contract, versioning, and error model for a
  five-endpoint application.
- *Legacy Blazor Server (`_Host.cshtml` + `blazor.server.js`)* — rejected: superseded by Blazor Web App
  in this framework version; choosing it would require an amendment to keep the SDK out of date.
- *Static server rendering only (no interactivity)* — rejected: the seat map is inherently interactive,
  and FR-016 forbids changing the visitor's journey.

---

## R2. Database context lifetime inside long-lived server circuits

**Decision**: Register `AddDbContextFactory<TicketingDbContext>` and create a short-lived context per
operation. `TicketingService` is scoped per circuit and holds no context between calls.

**Rationale**: An interactive server circuit lives as long as the browser tab, so a circuit-scoped
`DbContext` is a long-lived, non-thread-safe object with an ever-growing change tracker — exactly the
failure mode that produces "what I bought vanished" or "the seat I see is stale" bugs, in an
application whose whole purpose is trustworthiness of state. A factory per operation makes the unit of
work explicit and short. It is also the composition that EF Core documents for Blazor Server.

**Alternatives considered**:

- *`AddDbContext` (scoped)* — rejected: scoped in Blazor Server means per circuit, i.e. long-lived; the
  same context would be shared by unrelated user actions and concurrent render callbacks.
- *`AddDbContextPool`* — rejected: pooling bounds how long a context is *reused* by a request, not how
  long it lives in a circuit; the lifetime problem remains.
- *Singleton context with a lock* — rejected: serialises every visitor behind one object and turns a
  read into a contention point; also hides the unit of work from the code.

---

## R3. How the schema is created on a first start

**Decision**: Use EF Core migrations, applied at application start before seeding, plus a design-time
context factory so the tooling can build a context.

**Rationale**: Migrations create the store and its schema when the store does not exist yet — which the
spec defines as "empty, so build the venue" (FR-011) — and they are the only option that keeps working
when the schema grows. The project already expects growth: the spec's Assumptions name a later
seat-hold feature, which will add a table. With migrations, an existing store picks that table up on
the next start; without them, every existing store would silently keep the old schema and fail at
runtime — a restart-shaped failure in the exact feature that exists to make restarts safe.

**Alternatives considered**:

- *`Database.EnsureCreated()`* — rejected for the reason above: it creates a schema only when the
  database file is absent and never evolves it. It is genuinely simpler, and it was the tempting
  choice for a 120-row demo, but it converts the next feature into a data-loss decision.
- *Ship a pre-built `.db` file with the application* — rejected: the store is runtime state; committing
  it would also put a binary blob under version control and blur "fresh venue" with "someone's sales".
- *Create the schema with raw SQL on start* — rejected: hand-written DDL drifts from the EF model
  immediately and has no upgrade path at all.

---

## R4. What exactly a fresh venue contains

**Decision**: A fresh venue is built from an explicit, pinned inventory — no randomness at runtime.

- Event: `Midnight Frequencies Tour` by `The Static Fields`, 2026-11-21 20:00, `Grand Hall Auditorium`.
- Rows `A`–`J`, 12 seats each, 120 seats.
- Prices by row: `A` = 150, `B`–`D` = 120, `E`–`H` = 90, `I`–`J` = 60.
- Taken seats: the 28 seats listed in
  [contracts/venue-seed-contract.md](./contracts/venue-seed-contract.md); every other seat is available.

**Rationale**: FR-012 requires the fresh venue to be reproducible *and* to mark the same seats as taken
as the venue does today. Today's set comes from `new Random(20261121)` with `roll < 22`. Reproducing it
by keeping the same seeded generator in production code would work but leaves the requirement resting
on framework behaviour: the expected result would be recomputed by the same code that is under test, so
a test could never fail. Pinning the resulting 28 seats makes the requirement an observable fact, makes
the seed a plain constant (Principle V — no randomness where none is needed), and satisfies the
constitution's demand that initial data be seeded deterministically.

**Alternatives considered**:

- *Keep `Random(20261121)` in the seeder* — rejected: the test would compare the generator against
  itself; and .NET's compatibility guarantee for seeded `Random` is a promise about the past, not a
  contract worth depending on for stored data that must not change.
- *Replace the seed with an obviously synthetic pattern (every fifth seat taken)* — rejected: FR-012
  requires the same taken seats as today, and this would change what visitors see beyond the two
  changes the spec allows.
- *Seed a completely empty venue* — rejected: User Story 3 describes a venue with "about a fifth
  already taken", and an empty hall would make the first screen unrepresentative.

---

## R5. Making a purchase atomic, without concurrency protection

**Decision**: One explicit transaction per purchase. Inside it: re-read the selected seats, verify each
is still available, insert the purchase and its lines, mark the seats taken, commit. If any selected
seat is no longer available, change nothing and report it to the visitor.

**Rationale**: FR-008 and SC-005 demand all-or-nothing: a purchase that leaves an order without seats,
or seats taken without an order, must never be visible. A transaction gives exactly that. Verifying
inside the transaction also means the common double-submission case (a visitor double-clicks, or two
tabs of the same visitor) is caught rather than recorded twice.

**What this deliberately does not do**: it does not use concurrency tokens or a serialisable lock, so
two *different* visitors submitting the same seat in the same instant may both succeed. The spec
accepts this explicitly and scopes SC-004 to sequential use; the plan keeps the gap visible rather than
half-closing it with a mechanism nobody has specified.

**Alternatives considered**:

- *Optimistic concurrency tokens on the seat row* — rejected for now: it is a genuine improvement, but
  it is the seat-hold feature's job, and adding it here would quietly change the failure behaviour the
  spec deliberately left as a known gap.
- *A serialisable transaction or an application-wide lock* — rejected: it would make the guarantee
  stronger than specified while serialising every purchase, and it is untestable at this scale in a way
  that would mislead future readers about what is actually protected.

---

## R6. How the confirmation is produced (FR-014)

**Decision**: Completing a purchase returns the new purchase's identifier. The page then re-reads that
purchase from the store together with its lines, and passes the resulting `PurchaseReceipt` (seats,
amount, timestamp) to the confirmation dialog.

**Rationale**: FR-014 says the confirmation must be derived from the durable record, and the honest
reading of "derived from the durable record" is "read back out of it". Re-reading also proves the
round trip that User Story 1 cares about, at the only moment a visitor could notice the difference.

**Alternatives considered**:

- *Return the just-saved objects and render them* — rejected: it is a copy kept in memory, which is the
  behaviour the spec's Principle II obligation removes.
- *Re-render the whole page and show the last purchase* — rejected: it changes the journey (FR-016) and
  needs a "which purchase is mine" notion that the spec explicitly refuses.

---

## R7. Whether a page that stays open must follow the venue live

**Decision**: No push between visitors. A page shows the venue as of its last read; freshness comes from
reading. Every choice is re-checked against the store before it is accepted, and every purchase is
re-checked inside its transaction, so a stale screen can never become a wrong sale.

**Rationale**:

- **The spec was asking two different questions.** FR-010 is about *authority*: no visitor may hold a
  private or independently generated view. Reading the store satisfies that, with or without push.
  SC-003 and User Story 2 scenario 1 were about *freshness*: how fast an already-open page catches up.
  Only the second property needs a notification mechanism, and it is not the property the feature is
  about.
- **The criterion could not measure what the mechanism bought.** No design removes the lag between a
  commit and a screen; a notifier would narrow it, not eliminate it, while SC-003 read as "zero
  discrepancies at every instant" is unachievable by construction.
- **The machinery was the least testable part of the feature.** A singleton raised inside one circuit and
  delivered to another runs the subscriber's handler on the publisher's thread, where
  `ComponentBase.StateHasChanged` is not valid — the subscribers (`SeatMap`, `SelectionSummary`) would
  have to marshal through `InvokeAsync`, every call would need wrapping so that one circuit's exception
  cannot surface inside another visitor's purchase, and none of that is reachable by the automated tests
  the constitution requires (SC-009 asks for zero manual verification steps). Paying an untestable cost
  for an unmeasurable gain is the trade this decision refuses.

**Alternatives considered**:

- *A singleton notifier with `InvokeAsync` marshalling* — rejected for the reasons above: verifiable only
  through a new component-testing dependency or by hand, and the part most likely to fail silently.
- *SignalR broadcast* — rejected: the same outcome through a public transport surface the application
  does not otherwise need.
- *Polling from the browser on a timer* — rejected: it puts freshness logic in the UI and spends round
  trips to discover that nothing changed.

**Accepted consequences**, all now written into the spec: User Story 2 scenario 1 and SC-003 speak of
"each visitor who reads the venue", a stale-page edge case spells out the refusal path, and an Assumption
records that freshness is not pushed. The visible cost is that a visitor may be told a seat went away
just as they chose it — an outcome the design already handles, not a new failure.

---

## R8. Where the store lives and how it is configured

**Decision**: Connection string `Data Source=App_Data/ticketing.db`, configurable under
`ConnectionStrings:Ticketing` in `appsettings.json`, resolved relative to the application's content
root so the launched app always finds the same file regardless of the working directory. `App_Data/` is
added to `.gitignore`.

**Rationale**: The store is runtime state, never source; keeping it inside the repository tree keeps a
demo's state discoverable and deletable in one step ("delete this folder to start fresh"), which
matters for User Story 3 and for anyone validating it. Resolving from the content root avoids the
classic "works with `dotnet run`, loses data under the launcher" bug.

**Alternatives considered**:

- *A path in the user profile or program data* — rejected: it hides the demo's state from the person
  validating it and makes "start fresh" require hunting through the file system.
- *In-memory SQLite* — rejected: it is the behaviour the feature exists to remove.
- *SQL Server LocalDB* — rejected: a heavier dependency for 120 rows, and the constitution permits
  SQLite specifically.

---

## R9. What a visitor sees when the store cannot be read (FR-015)

**Decision**: Loading the venue is a single guarded operation whose outcome the service exposes
(loaded / failed). `Pages/Home.razor` renders either the workspace or an error panel; when loading
failed, the seat map, the selection summary, and the buy action are not rendered at all.

**Rationale**: FR-015 and SC-008 ask for an error and explicitly forbid presenting the venue as fully
available. Not rendering the map is the only presentation that cannot be mistaken for a working page,
and it is directly assertable by a test that serves the page against a broken store.

**Alternatives considered**:

- *A Blazor `<ErrorBoundary>` around the workspace* — rejected: it catches render failures after the
  fact and would still require a decision about showing seats; an explicit load outcome is simpler and
  testable without provoking an exception.
- *Render the map with every seat disabled* — rejected: SC-008 measures "zero attempts show the venue as
  fully available", and a full grid of seats is exactly the picture that must not appear.

---

## R10. Testing restart survival without a human

**Decision**: Two levels, both run by `dotnet test`:

1. **Store level** — build a context factory and service over a temporary SQLite file; seed, purchase,
   dispose everything, rebuild against the same file, assert the seats are still taken and the purchase
   is still retrievable with the same amount.
2. **Host level** — build the real application with `WebApplicationFactory<Program>` over the same file,
   purchase through the service resolved from the host's services, dispose the host, build a second host
   over the same file, and assert the same facts plus that the served page no longer offers the bought
   seat.

**Rationale**: FR-017 and SC-009 require the guarantees to be confirmed automatically on every change,
"without manual interaction with the interface" — the level-2 test is what makes that honest, because
it exercises the real composition root (migrations, seeding, DI wiring) rather than a hand-built
context. Level 1 stays because it isolates domain rules and fails with a readable message.

**Alternatives considered**:

- *Only store-level tests* — rejected: they would pass even if the application registered the context
  wrongly or pointed at a different file, which is the most likely way this feature breaks in reality.
- *Browser automation (Playwright/Selenium)* — rejected: a new heavyweight dependency to assert what a
  served HTML response already reveals, and the accessibility work it would naturally belong with is
  explicitly out of scope.

**Implementation note**: `WebApplicationFactory<Program>` needs an accessible entry point, so
`Program.cs` ends with `public partial class Program { }`.

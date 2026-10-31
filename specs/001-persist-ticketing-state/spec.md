# Feature Specification: Persist Ticketing State

**Feature Branch**: `001-persist-ticketing-state`

**Created**: 2026-10-25

**Status**: Draft

**Input**: User description: "реализовать персистентость при покупке билетов. приложение должно переживать перезапуск"

## Clarifications

### Session 2026-10-25

- Q: How many seat states must the application store after a restart — two (available and taken) or all the states the interface distinguishes today, including "unavailable"? → A: Two stored states — available and taken. "Selected" remains a session-only overlay on a seat whose stored status stays available, and "unavailable" is removed from the model and from the map, so a visitor sees three states: available, selected, taken.
- Q: What should happen to the pre-existing accessibility gaps in the elements this feature changes — the confirmation dialog and the seat map: close them inside this feature or in a separate one? → A: In a separate one. This feature MUST NOT be required to close those gaps; they are a documented exception under the constitution's Quality Gate 4 and MUST be tracked as separate work.
- Q: What should become of the seats a freshly created venue marks as "unavailable", now that the state no longer exists? → A: They become available. Nothing is withheld from sale, so a fresh venue marks the same seats as taken as the venue does today and puts every other seat on sale.
- Q: Must the confirmation carry something the visitor can present later — a short purchase code — or is what they see right after buying enough? → A: Nothing presentable. The purchase gets no public code, and the confirmation stops telling the visitor to present it at the door, so it claims only what a stored record can back.
- Q: Does a store that does not exist yet count as empty — the application creates it and fills the venue — or as an error requiring the store to be prepared beforehand? → A: It counts as empty. A missing store is not an unreadable store: on a first start the application creates the store and the venue together.

### Session 2026-10-31

- Q: Must a page that stays open follow the venue live as other visitors buy, or is it enough that every visitor sees the same venue once the page has read it? → A: Reading is enough. There is no live push between visitors: a page shows the venue as of its last read, and every choice and every purchase is re-checked against the store, so no sale is ever decided from a stale view.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A purchase is still there after a restart (Priority: P1)

A visitor chooses seats, buys them, and leaves. The application is restarted — a redeploy, a crash,
a planned restart. The visitor comes back, and the seats they bought are still taken, and the money
they paid is still recorded. Nothing they did is lost.

**Why this priority**: This is the requirement in its plainest form. Today the whole venue exists
only inside one browser session, so a restart erases every sale. Until this holds, the application
cannot take a single real booking.

**Independent Test**: Buy at least two seats, restart the application, reopen it. The seats are
still taken and the purchase is still retrievable, with the same seats and the same amount.

**Acceptance Scenarios**:

1. **Given** a visitor has bought seats, **When** the application is restarted and reopened,
   **Then** every seat in that purchase is still shown as taken.
2. **Given** a visitor has bought seats, **When** the application is restarted, **Then** the
   purchase is still recorded with the same seats and the same total amount.
3. **Given** several purchases have been made, **When** the application is restarted, **Then** all
   of them are still recorded — none is dropped, duplicated, or altered.
4. **Given** a completed purchase, **When** the visitor views the confirmation, **Then** it reflects
   the recorded purchase rather than a copy kept in the visitor's session.

---

### User Story 2 - Every visitor sees the same venue (Priority: P2)

Two people open the ticketing page at the same time, on different devices. They see the same seat
map. When one of them buys a seat, that seat is taken for both of them — it is sold once, not once
per visitor.

**Why this priority**: It follows directly from P1 and is the reason a restartable store matters. A
store that survives a restart but is read per-visitor would still let the same seat be sold twice
without anyone noticing.

**Independent Test**: Open the page in two independent visitors. Buy a seat in one, refresh the
other. The seat is taken in both.

**Acceptance Scenarios**:

1. **Given** two independent visitors have the page open, **When** each of them opens or refreshes
   the map, **Then** both see the same set of available and taken seats.
2. **Given** a visitor buys a seat, **When** another visitor reloads the map, **Then** that seat is
   shown as taken for them too.
3. **Given** a seat is recorded as taken, **When** any visitor tries to choose it, **Then** it cannot
   be chosen and cannot appear in a purchase.

---

### User Story 3 - A fresh installation is a complete, correct venue (Priority: P3)

The application is installed for the first time, against an empty store. A visitor opens it and
sees a complete venue: all rows and seats are present, priced correctly, and about a fifth of them
already taken. If the application is installed again against the same store, the venue is not
duplicated or reset.

**Why this priority**: The venue currently exists only as something generated in code. Once it
becomes stored data, "who creates it and when" becomes a real behaviour with real failure modes — an
empty or doubled venue would be worse than the current situation.

**Independent Test**: Start against an empty store and confirm the complete venue appears; restart
against the same store and confirm nothing is duplicated or reset.

**Acceptance Scenarios**:

1. **Given** an empty store — including one that does not exist yet — **When** the application
   starts, **Then** the event and the complete seat inventory are present, each seat priced according
   to its row.
2. **Given** an empty store, **When** the application starts twice, **Then** each seat exists exactly
   once — no duplicates.
3. **Given** a populated store, **When** the application starts, **Then** existing seats, statuses,
   and purchases are left exactly as they were.
4. **Given** an empty store, **When** the initial inventory is created repeatedly, **Then** the
   result is identical every time — same seats, same statuses, same prices.

---

### Edge Cases

- **The application restarts in the middle of a purchase**: the purchase is either fully recorded —
  the purchase and its seats together — or it left no trace. A half-committed purchase (a purchase
  without seats, or seats taken without a purchase) MUST NOT be visible to any visitor.
- **The store cannot be read**: the application MUST report a clear error and MUST NOT present the
  venue as fully available. Showing every seat as free when the store is unreadable would look like
  a working page while silently discarding all real sales. A store that does not exist yet is not this
  case — it is treated as empty (FR-011).
- **The store is empty but the application expects content** (for example the store was cleared):
  the venue is created anew, and this MUST be visibly a fresh venue rather than a mix of old and new.
- **The price of a row changes after seats have been sold**: seats already sold keep the price they
  were sold at; the recorded purchase amount MUST NOT change retroactively.
- **Seats are selected but the visitor never buys**: that selection is not remembered across a
  restart, and the visitor returns to an empty selection without an error. Persisting an unfinished
  selection is a different behaviour (a seat hold) and is out of scope.
- **Two purchases for the same seat are submitted at the same instant**: this iteration does not
  defend against it. See Assumptions — the guaranteed protection is sequential only.
- **A visitor keeps the page open while someone else buys**: their map may still show that seat as
  free. Choosing it is refused against the store, the seat is dropped from their selection, and they
  are told it is gone. Nothing is ever bought from a stale view.
- **The venue is larger than the visible area**: the stored inventory is not truncated — every stored
  seat is reachable, regardless of how many there are.
- **A restart happens repeatedly and rapidly**: repeated restarts do not accumulate duplicates,
  duplicate inventory, or duplicate purchases.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The event being sold MUST be stored durably, and MUST survive a restart of the
  application unchanged.
- **FR-002**: The complete seat inventory — every row and every seat — MUST be stored durably and
  MUST survive a restart unchanged.
- **FR-003**: Each seat's price MUST be stored durably, and the price a visitor sees MUST equal the
  stored price for that seat.
- **FR-004**: Each seat's stored status MUST be exactly one of two values — available or taken — and
  MUST survive a restart unchanged. No other seat status is durable.
- **FR-005**: Confirming a purchase MUST create a durable record of that purchase containing the
  seats bought, the amount paid, and when it happened.
- **FR-006**: The recorded amount of a purchase MUST equal the sum of the stored prices of the seats
  it contains at the moment of purchase, and MUST NOT change afterwards.
- **FR-007**: A seat recorded as taken MUST NOT be purchasable again.
- **FR-008**: A purchase MUST take effect as a single unit: it either records the purchase and marks
  all of its seats as taken, or it changes nothing.
- **FR-009**: Restarting the application MUST NOT change any stored state — event, inventory, prices,
  seat statuses, or recorded purchases. The state after a restart MUST be identical to the state
  before it.
- **FR-010**: All visitors MUST read the same stored state; no visitor may see a private or
  independently generated view of the venue.
- **FR-011**: Initial inventory MUST be created only when the store has none, and MUST NOT be
  duplicated, reset, or partially recreated when the store already has content. A store that does not
  exist yet counts as empty, not as unreadable: on a first start the application creates the store and
  the venue together.
- **FR-012**: Creating the initial inventory MUST be reproducible: the same starting conditions MUST
  produce the same inventory, statuses, and prices every time. The inventory it creates MUST contain
  only the two stored statuses, and the seats it marks as taken MUST be the seats the venue marks as
  taken today; every other seat MUST be created available. No seat is withheld from sale.
- **FR-013**: A visitor's unfinished seat selection MUST NOT be part of the durable state; it is a
  session-only overlay on a seat whose stored status remains available.
- **FR-014**: The purchase confirmation shown to a visitor MUST be derived from the durable record of
  that purchase.
- **FR-015**: When the stored state cannot be read, the application MUST report the problem to the
  visitor and MUST NOT present the venue as fully available.
- **FR-016**: The visitor-facing behaviour of the seat map, selection summary, and confirmation MUST
  be preserved; this feature changes where the state lives and how long it lasts, not what the
  visitor does. Exactly two visible changes are intended. First, the unavailable state is removed: a
  seat that is not recorded as taken shows as available, so the map presents exactly three states —
  available, selected, taken. Second, the confirmation stops instructing the visitor to present it at
  the door (see FR-018).
- **FR-017**: The guarantees in FR-001 to FR-011 MUST be confirmable automatically, without manual
  interaction with the interface, and MUST be confirmed whenever the project changes.
- **FR-018**: The purchase confirmation MUST claim only what a stored purchase record can back: the
  seats bought, the amount paid, and that the purchase is complete. It MUST NOT instruct the visitor
  to present the confirmation at the door, and MUST NOT imply any ticket artefact the application does
  not issue.

### Key Entities

- **Event**: The performance being sold — its name, artist, date, and venue. One event exists in this
  iteration; it is stored rather than defined in code.
- **Seat**: A single seat in the venue, identified by its row and number. Holds the price it is sold
  at and its stored status — available or taken. A visitor's current selection is not part of this
  entity; the map's three visible states are available, selected, and taken.
- **Purchase**: A completed transaction. Records the seats bought, the total amount paid, and when it
  happened. The visitor's confirmation is derived from this record.
- **Purchased seat line**: The link between a purchase and each seat it contains, carrying the price
  paid for that seat at that moment, so a later price change cannot alter history.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: After a restart of the application, 100% of seats that were taken before the restart are
  still taken — zero sales lost.
- **SC-002**: A purchase completed before a restart is retrievable after it in 100% of attempts, with
  the same seats and the same amount.
- **SC-003**: Two independent visitors who each read the venue report identical availability — zero
  discrepancies, checked across the whole inventory. A page opened earlier and not read again may lag
  behind, but no sale is ever decided from that lag: zero purchases are recorded for a seat that was
  already taken when the purchase was attempted.
- **SC-004**: Zero occurrences of a seat recorded as taken being purchased a second time during
  sequential use.
- **SC-005**: A purchase interrupted before completion leaves zero seats taken and zero recorded
  purchases — zero partial states observed across repeated attempts.
- **SC-006**: Starting against an empty store produces the same inventory every time — identical
  seat count, statuses, and price tiers across repeated runs, with zero variance.
- **SC-007**: Starting against a populated store adds zero duplicate seats and resets zero existing
  statuses or purchases.
- **SC-008**: When the stored state is unreadable, 100% of attempts report an error and zero attempts
  show the venue as fully available.
- **SC-009**: All guarantees above are confirmed automatically on every change, requiring zero manual
  verification steps.
- **SC-010**: A visitor's observable journey — choosing seats, seeing the total, buying, reading the
  confirmation — is unchanged: the same number of steps as before this feature. The visible changes
  are limited to those named in FR-016 — the unavailable state disappears and the confirmation no
  longer tells the visitor to present it at the door — and neither adds nor removes a step.

## Assumptions

- This feature requires project constitution version 2.0.0, which withdraws the previous
  "in-memory only" and "client-only" constraints. The amendment is already in place.
- A single event continues to be sold. Multiple events are out of scope.
- The application runs as one instance reading one store. Clustering, replicas, and horizontal scaling
  are out of scope, and no guarantee is claimed for them.
- **Concurrent protection is sequential only.** A seat that has been recorded as taken cannot be
  bought again afterwards. Two purchases submitted for the same seat at the same instant may both
  succeed; this is a knowingly accepted gap for this iteration, recorded here so it is not
  accidental, and is expected to be closed by a later seat-hold feature.
- An unfinished seat selection is not durable, and seat holds with a time limit are out of scope.
- There is no authentication and no visitor identity: purchases are not tied to an account, and any
  visitor can buy anonymously.
- No real payment is taken; a purchase is recorded without money changing hands.
- All stored content is synthetic. No real personal, payment, or credential data is introduced.
- The existing visual design and interaction of the interface are preserved, apart from the two
  changes named in FR-016: the seat map no longer shows an unavailable state, and the confirmation no
  longer tells the visitor to present it at the door.
- **Accessibility remediation is out of scope.** The interactive UI has known accessibility gaps today
  — the confirmation dialog does not trap focus, cannot be dismissed from the keyboard, and does not
  restore focus on close, and the seat map overflows horizontally at narrow widths instead of
  scrolling. This feature rewires that dialog and that map to the store but MUST NOT be required to
  close those gaps. They are a documented exception under the constitution's Quality Gate 4 and MUST
  be tracked as separate work; Principle IV stays unsatisfied until that work is done.
- **Freshness is not pushed.** A page shows the venue as of its last read and does not follow changes
  made by other visitors until it reads again. Every choice and every purchase is re-checked against
  the store, so this affects what a visitor sees, never what they are sold.
- The venue keeps its current shape and pricing: 10 rows, 12 seats per row, and four price tiers
  assigned by row.
- Nothing is withheld from sale. The seats the current venue marks as unavailable become available, so
  a fresh venue shows about 22% of its seats as taken — the same taken seats as today — and the rest
  on sale.

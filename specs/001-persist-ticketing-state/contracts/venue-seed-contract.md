# Contract: the fresh venue

What a first start against a store that does not exist yet MUST produce (FR-011, FR-012, SC-006), and
what a later start MUST produce unchanged (SC-007).

This is a constant, not a computation. Nothing here may depend on a random number generator, the
current time, or the machine it runs on.

---

## 1. Event

| Field | Value |
|---|---|
| Name | `Midnight Frequencies Tour` |
| Artist | `The Static Fields` |
| Starts at | 2026-11-21 20:00 (local time of the venue, stored with its offset) |
| Venue | `Grand Hall Auditorium` |

## 2. Inventory

10 rows (`A` through `J`), 12 seats per row, 120 seats in total. Seat numbers run `1`–`12` in every row.

| Rows | Price per seat |
|---|---|
| `A` | 150 |
| `B`, `C`, `D` | 120 |
| `E`, `F`, `G`, `H` | 90 |
| `I`, `J` | 60 |

## 3. Taken seats — exactly 28

These seats are created as **taken**. Every other seat is created **available**.

```
A3  A9
B7  B12
C6  C7
D6  D10 D12
E1  E4  E6  E12
F10
G2  G6  G8
H3  H6  H9
I4  I6  I8  I10
J3  J6  J8  J9
```

Counts by row: `A` 2, `B` 2, `C` 2, `D` 3, `E` 4, `F` 1, `G` 3, `H` 3, `I` 4, `J` 4 — 28 in total,
i.e. 23.3% of the venue, the "about a fifth already taken" of User Story 3.

**Provenance**: this is precisely the set the current application produces from `new Random(20261121)`
with `roll < 22`, kept because FR-012 requires the fresh venue to mark the same seats as taken as the
venue does today. The set is written out here so a test can compare against a fact rather than against
the generator that produced it. The seats the current application marks *unavailable* (the 12 remaining
rolls between 22 and 32) become available — nothing is withheld from sale.

## 4. What a start MUST do

| Store state | Required behaviour |
|---|---|
| Does not exist | Create it, create the schema, then seed exactly the venue above |
| Exists, no event | Seed exactly the venue above |
| Exists, has the event | Change nothing: no new seats, no status resets, no price changes, no re-seeding |
| Exists, unreadable | Report the failure; show no seats (FR-015, SC-008) — a missing file is *not* this case, it counts as empty |

## 5. What a test must be able to assert

1. A fresh store yields exactly 120 seats, each with the price of its row.
2. A fresh store yields exactly the 28 taken seats above — no more, no fewer, no others.
3. Seeding twice yields the same 120 rows, not 240.
4. Seeding over a store where a seat has since been bought leaves that purchase and that seat alone.

# Setting up the Related Party Transaction module

What has to be true before anyone can raise a transaction, in the order it has to be done. The
module itself is built ([design](rp-transaction-design.md), [workflow](rp-transaction-workflow.md));
this is the configuration it needs to run.

Steps 0, 2 and 3 are required. Steps 1 and 4 are about making the module useful rather than making
it work.

---

## 0. Deploy — migrations, then procedures, in that order

```bash
git pull origin claude/net10-upgrade
```

1. **Apply the EF migrations.** Starting the app against the database does it
   (`Database.MigrateAsync` runs at startup), or `dotnet ef database update`.
2. **Then run `scripts/stored-procedures.sql`** against the CGS database.

The order matters and the script enforces it: SQL Server resolves column names when a procedure is
created, so running it against a database whose schema is behind gives a run of "Invalid column
name" errors while every other procedure deploys happily. The guard at the top of the file checks
for the newest columns and refuses, loudly, rather than half-applying.

The latest migration this module needs is `AddDelegationOfAuthorityAndGovernanceDecision`.

**Email.** Notifications go out through SQL Server Database Mail. With `Smtp:SqlDbMailProfile`
unset, everything still works but the mail is only written to the log — the approver is never told
a transaction is waiting. See the outbound-email section of `README.md`.

---

## 1. The Related Party Master — who can be a counter-party

The counter-party dropdown reads the **standing Related Party Master**, grouped:

| Group | From |
| --- | --- |
| Group entities | Active companies on the register |
| Users | Active members |
| Relatives | Every relative in My Register |
| Members' companies | My Register → My Companies |
| Relatives' companies | The companies a relative holds |

**That is the whole list.** Declarations are not a source: a name declared once and never added to
My Register is not on the master, and offering it would say it is. If someone should be selectable,
add them to My Register.

This is reference data, maintained directly, so **it does not wait for a declaration cycle**. Add a
relative or a company in **My Register** and it is selectable on the next page load. Check what is
there at `/reports/related-party-master`.

Each option says why it is a related party — "Ahmed Khan — Nayyar Ali Khan's Spouse" — so the
requestor is not picking a bare name out of a list of two hundred.

> Until recently this list came from submitted COI declarations instead, which meant a fresh
> deployment had an empty dropdown and nobody could raise a transaction.
>
> One consequence of the change: a transaction already raised may name a counter-party that is no
> longer on the master — declared once and never registered, or since removed. The **amend** form
> keeps that name selectable for that transaction alone, labelled *no longer on the Related Party
> Master*, so amending the value or the description cannot silently change who the transaction was
> with. Nothing puts it back in the list for new transactions.

## 2. Entities — give each one an approving authority

**Admin Panel → Company → (entity) → Authority**

Every entity that will raise transactions needs:

- **Authorized / approving authority** — the person who decides on its transactions. This is who
  the approver queue belongs to.
- **Delegate authority** — who acts when they cannot.

Either may be set to **Not applicable**, but one of the two answers must be given; the form refuses
to save an entity with neither a person nor that flag.

**Without an approving authority, submission is refused** with: *"No Approver is configured for your
entity. Ask an Administrator to set one on the Company record."* This is the second most common
reason the module appears broken.

---

## 3. People — access and positions

**Admin Panel → User Management → (member)**

Two separate things, often confused:

| Setting | Where | What it does |
| --- | --- | --- |
| **Related Party Transaction** access | "Role Assigned" checkboxes | Shows the RP Transactions menu and lets them raise one. Administrators see it regardless. |
| **RP Transaction Role** | Its own dropdown | The workflow position: CCAO, CFO, COO, MD & CEO. Nothing to do with Administrator / Normal User. |

Set these:

- **Everyone who raises transactions** → tick *Related Party Transaction*.
- **The approver** → they do not need the access flag to decide, but they do to see the menu. Tick
  it. They are the approver because they are named on the entity (step 2), not because of a role.
- **At least one person → RP Transaction Role = CCAO.** `/rp-transactions/ccao-review` is restricted
  to the CCAO position (and administrators). Without one, escalated transactions have nowhere to go
  and nobody is emailed.
- **Optionally one or more → CFO.** They receive copies of escalation and decision notifications.

Any number of people may hold a position; all of them are notified.

> **COO and MD & CEO exist in the dropdown and nothing routes to them.** The offline MD&CEO decision
> is recorded by the CCAO on the review screen (step 5 of the flow), not routed in the app. Set them
> if they are useful as a record; do not expect a queue.

---

## 4. Delegation of Authority — optional, but this is what makes routing real

**Admin Panel → Delegation of Authority**

Without a matrix, an entity behaves exactly as it did before the feature existed: every transaction
goes to its single approving authority whatever it is worth. That is a valid configuration — the
screen says so — but four things stay switched off.

Per entity, set:

- **Value bands** — which body must ultimately approve a transaction of that size (Approver only;
  Approver then CCAO; then Audit Committee; then Board; then General Assembly). The coverage panel
  refuses gaps, overlaps and a capped top band, because a gap silently routes whatever falls into it
  nowhere.
- **The approver's own limit** — above it a transaction goes **straight to the CCAO at submission**
  and never reaches the approver. This is separate from the bands: the bands say *which body must
  approve*, the limit says *whether the approver is in the path at all*.
- **Always escalate when the approver is the related party** — on by default.
- **The approver's deadline** — days before it escalates by itself. Blank uses the app-wide 30.

What a saved matrix turns on: the routing preview on the raise form, the "above your limit" check on
the approver's screen, over-limit escalation at submission, and the per-entity deadline.

---

## 5. Check it end to end

Raise one real transaction and walk it through. Fifteen minutes, and it exercises every gate above.

| # | As | Do | Expect |
| --- | --- | --- | --- |
| 1 | A requestor | `/rp-transactions/new` | The counter-party list is grouped and populated from the master. |
| 2 | | Pick a counter-party, enter a value and a description | The Checks panel fills in: whether the counter-party is on the master, whose relationship it is, and where it will route |
| 3 | | Submit | The overlay appears, then the transaction shows under My Transactions as *Awaiting Approval* |
| 4 | The approver | `/rp-transactions/approvals` | It is in the queue, with an SLA meter counting to the 30-day escalation |
| 5 | | Add remarks, Approve | It leaves the queue; the CCAO is emailed |
| 6 | The CCAO | `/rp-transactions/ccao-review` → Awaiting Decision | It is there. Record the body, the meeting date, any abstentions, the minutes, and remarks. Approve. |
| 7 | | → Ready to Release | Tick the documentation confirmation, Release |
| 8 | Anyone | `/rp-transactions/register` | It is on the register. Export to Excel carries all twelve spec columns plus the decision. |
| 9 | Anyone | Click its reference anywhere | The detail page shows the whole history: who did what, when, with what remarks |

Also worth testing once: **Return to requestor** from step 5 — the requestor gets it back under My
Transactions, amends it, and saving resubmits it to the same approver.

---

## What will bite you

| Symptom | Cause |
| --- | --- |
| Counter-party dropdown is empty | Nothing in My Register and no entities or users — step 1 |
| "No Approver is configured for your entity" | No approving authority on the Company — step 2 |
| CCAO Review says "restricted to the CCAO position" | Nobody has RP Transaction Role = CCAO — step 3 |
| No emails arrive anywhere | `Smtp:SqlDbMailProfile` unset — step 0 |
| RP Transactions missing from the menu | Either the member lacks the *Related Party Transaction* access flag (step 3), or the whole group is switched off under **Admin Panel → Settings → Main sections**. Hiding a parent hides everything under it, so the RP Transactions card can read "5 of 5 shown" while nothing appears; it now says so instead. |
| Value does not change the route | No Delegation of Authority for that entity — step 4. Working as configured, not broken. |
| "Invalid column name" when deploying the procedures | Procedures run before migrations — step 0 |

## Housekeeping

- **Uploads** land in `wwwroot/uploads/rp-transactions`. They are not cleaned up; back them up with
  the database, because the register links to them.
- **The reminder job** (`RelatedPartyTransactionReminderHostedService`) runs hourly: weekly reminders
  to the approver, auto-escalation at the deadline, then weekly reminders to CCAO and CFO. It needs
  no configuration, but it does need the app to stay running.

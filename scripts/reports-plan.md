# Reports — plan

The reports still live in the old CG app, rendered by an RDLC report viewer at
`cg.dubaiinvestments.com/ReportViews/<Name>`. They are the last part of the system that has not
moved into CGTOOL, and they are visibly failing: the Insider Submission and RP & COI Submission
reports both print
`An error has occurred while processing TextBox 'textBox17'` in place of every `ModifiedOn` and
`ModifiedBy` value, because the RDLC expression refers to a field the dataset no longer returns.
That is not a bug to patch in the RDLC. The data it wants is already in CGTOOL's own model, on
entities this codebase owns and tests, and every other screen that reads them has already moved.

So: rebuild the report suite as CGTOOL pages, against the CGTOOL model, in the layout of the
Users Report design in `scripts/reports/users-report-design.dc.html`.

## What the design settles

The design is the first statement of what a *printed* CGTOOL report looks like, and it is a
departure from what the report pages do today. Today a report is a filter bar over a web table,
with a print preview that reflows that same table into a modal. The design instead draws the
page the reader will actually hold:

- **A branded sheet**, 1123×794 at A4 landscape, with the navy/gold Dubai Investments header band
  carrying the report title, the date, who prepared it and the record count.
- **A stat band** across the top of page 1 — four figures that answer the question the report is
  opened to answer, before any row is read.
- **The filter line is chrome, not content.** It is `noprint`; what prints is the sheet, plus a
  one-line `Filters: …` statement so a printout says what it was filtered to. A printed report
  that does not say what it excluded is not evidence of anything.
- **Real pagination.** Page 1 carries the full masthead; pages 2+ carry a slim continued header.
  Every page carries `Confidential · For internal compliance use only`, the domain, and
  `Page N of M`. The RDLC viewer gave us this and the current CGTOOL print modal does not.
- **Excel and PDF as peers** of each other in the toolbar, not an afterthought.

The design also puts the three access flags inline as editable True/False pills behind an
"Edit access" mode. That is worth keeping, but it is an administrative action inside a report,
so it needs the audit trail every other member edit gets — see *Decisions* below.

## Architecture

Four new shared components under `Components/Shared/Reports/`, so a new report is a query plus a
column list rather than 400 lines of copied chrome:

| Component | Responsibility |
| --- | --- |
| `ReportSheet.razor` | The paginated A4 sheet: masthead on page 1, continued header after, footer with page N of M. Takes a title, a stat list and the filter statement. |
| `ReportStatBand.razor` | The four-up figure band. |
| `ReportToolbar.razor` | Search, the filter slots a report supplies, Excel, Print/PDF. `noprint`. |
| `ReportWorkbook.cs` | The `.xlsx` writer: one sheet, frozen header row, auto-width columns, the filter statement in a note row above the table. |
| `ReportPdfBuilder.cs` | The `.pdf` writer, over the existing `DeclarationPdfWriter` in landscape. |
| `ReportViewer.razor` | The viewing chrome around the sheet: zoom, fit-to-width, page navigation, full-screen. `noprint`. |

Print CSS goes in `app.css` beside the existing `@media print` block rather than inline as the
design has it — the existing rules already solve the hard part (an overlay nested deep inside
`div.page > main > article.content` printing without its ancestors reserving space), and that
work should not be re-derived per report.

The pages stay `[Authorize(Roles = GovernanceRoles.Administrator)]` and keep their
`/reports/<name>` routes, so the Reports nav group in `NavMenuCatalog` gains entries rather than
changing shape.

## Inventory

Legacy reports seen so far, and what CGTOOL has against each:

| Legacy report | CGTOOL today | Work |
| --- | --- | --- |
| `UsersReport` | — nothing | **New page.** The design in hand. Member list with company, department and the access flags. |
| `InsiderSubmissionReport` | `/reports/insider-declarations` | Exists, but has no summary band and does not show `ModifiedOn`/`ModifiedBy` — the two the legacy report fails on. Add the stat band (Total Submissions / Non Submissions / Total Users) and both columns. |
| `InsiderSubmissionDetailReport` | — nothing (the per-record print in the report page is close) | **New page or a mode of the above.** Per-person: Has NIN, Relatives Have NIN, holds shares in DI PJSC, and the Individual NIN Numbers grid. 304 pages in the legacy viewer, so it needs a name filter, which the legacy one has as a raw `%` box. |
| `InsiderSubmissionNinReport` | — nothing | **New page.** One row per declarant: user, department, company, submitted, has NIN, NIN, number of shares. |
| `RelatedPartySubmissionReport` | `/reports/related-party-coi-declarations` | Same shape as the Insider one and **failing the same way** on `ModifiedOn`/`ModifiedBy`. Same fix: stat band plus both columns. |
| `RelatedPartySubmissionDetailReport` | the per-record print in the report page | Close, but the legacy one is its own report with a name filter over 152 pages. Two grids: Related Parties (Legal Name / Nature of Business / Nature of Holding) and Conflict of Interest (Legal Name / Nature of Business / Nature of My Interest). |
| `InsiderTrading` | `/investor-relations/share-trading` | **New report over an existing screen.** NIN, Name of Share Holder, Type, Date, Value, Volume, filtered by start date, end date and file date. |

The model already carries everything these need — `InsiderDeclaration.HasNin`, `NinNumber`,
`HoldsShares`, `NumberOfSharesHeld`, `RelativesHaveNin`, and `InsiderDeclarationNinHolder`
(`Relationship`, `NameOfShareHolder`, `NinNumber`) for the Individual NIN Numbers grid. The
Insider Trading report reads `Transaction` (`Instrument`, `Side`, `Quantity`, `Amount`,
`TradeDate`, `FiledDate`) joined to the declarant's NIN. No migration is needed for any of them. `Member` already carries the four access flags the
Users Report shows: `ConflictOfInterestAccess`, `InsiderTradingAccess`,
`RelatedPartyRegisterAccess`, `RelatedPartyTransactionAccess`.

This table is the part of the plan still being filled in — more legacy reports are being
inventoried and get added here as they are.

## Decisions

**Four access columns, not three.** The legacy Users Report shows three. CGTOOL has four flags;
`RelatedPartyRegisterAccess` is missing from the legacy report, and a report on who can reach
what should not quietly omit one of the four things they can reach.

**Inline access editing is audited.** The design lets an administrator toggle access in the
report. Editing goes through `MemberWriter` and the existing audit path, exactly as the Member
screen does — not a direct `SaveChanges` from a report page. If that proves awkward, the toggle
comes out and the report links to the member instead. A silent access change is the one outcome
not on the table.

**Three ways out of every report, and they agree with each other.**

- *Print* — the browser prints the sheet as shown, via the `@media print` rules already in
  `app.css`. The viewing chrome is `noprint`; the sheet, its masthead and its footer are what
  lands on paper.
- *PDF* — generated on the server with PDFsharp, not by asking the browser to print to file. A
  file that arrives by email must look the same for everyone who opens it, and the browser's
  print-to-PDF stamps its own header and margins. `DeclarationPdfWriter` already draws the
  navy/gold band, the paginated `DataTable` and the "Page X of N" footer for the declaration
  PDFs; it gains a landscape option and the reports use it, so the report PDFs and the
  declaration PDFs are recognisably the same documents.
- *Excel* — a real `.xlsx` via ClosedXML, not CSV. CSV loses the header formatting, loses the
  column widths on every open, and asks the reader to trust their locale for dates and numbers.
  The register page's hand-rolled CSV moves onto the same writer.

All three carry the same filter statement, so a PDF, a spreadsheet and a printout of the same
report can be compared and can be told apart.

**The legacy RDLC reports are not fixed.** Once a report is rebuilt, the old route is dead. It is
worth confirming with the business that nobody has bookmarked `/ReportViews/…` before the old app
is retired.

## Viewing the report

The RDLC viewer the reports are leaving had a real toolbar — page forward and back, jump to page,
zoom in and out, fit to width. Losing that is a downgrade even when everything else improves, so
`ReportViewer` carries it:

- **Zoom** in fixed steps, plus **fit to width** and **fit whole page**, because an A4 landscape
  sheet is wider than most windows and the default of "shrink until it fits" makes a 12-column
  table unreadable.
- **Page navigation** — first/previous/next/last, a page number to type into, and `N of M`.
- **Full screen**, which is what the report is actually read in.
- The sheet stays a fixed 1123×794 at 100%; zoom scales it with a CSS transform rather than
  reflowing, so what is on screen is what prints and what the PDF contains.

## Build order

1. `ReportSheet` / `ReportStatBand` / `ReportToolbar` / `ReportViewer`, plus `ReportWorkbook` and
   `ReportPdfBuilder`, proven by rendering against the real `app.css` at A4 landscape and by
   opening a generated `.xlsx` and `.pdf`, before any report uses them.
2. **Users Report** — the design is in hand, and it is the one with no CGTOOL equivalent at all.
3. **NIN Report of Submissions** — a flat table, so it exercises the chrome with no per-record view.
4. **Insider Submission Report** — retrofit the stat band, add `ModifiedOn`/`ModifiedBy`.
5. **Insider Submission Detail** — the per-record sheet, with the name filter.
6. **RP & COI Submission** and **RP & COI Detailed Submission** — the same two shapes again, so
   they come cheaply once 4 and 5 exist.
7. **Insider Trading** — date-range filters rather than year/quarter, so it is the one that proves
   the toolbar takes arbitrary filter slots.
8. Retrofit the Related Party Register and Related Party Master reports onto the same chrome, so
   the suite is one thing.

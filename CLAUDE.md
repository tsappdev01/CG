# CGTOOL — working notes

DI Corporate Governance Tool. Blazor Web App on .NET 10, EF Core, SQL Server `CGS` on
**UATWEB01**, deployed to IIS by `azure-pipelines.yml`. `README.md` is the engineering record.

## Before you call a change done, find the other places it touches

> After a change is made, cross-check all related areas, modules and functionalities to
> identify and address any potential impact or dependencies.

That is a standing instruction from the people who own this system, not a style preference, and
it is here because it has caught something nearly every time it has been followed.

Most of what has gone wrong here was not a wrong line. It was a right line in one place and the
same thing left unchanged in three others, which nothing noticed because each place still worked
on its own. So a change is not finished when it compiles:

1. **Grep for what you changed** — the field, the label, the column, the rule — across
   `*.razor`, `*.razor.cs`, `Data/` and `scripts/`. Not for the symptom; for the thing itself.
2. **Ask what reads it.** A property is written in one place and read in five. A label is shown
   on a screen, printed in a PDF and exported in a report.
3. **Run it.** A check that was not run is a guess. `dotnet run --project src/CGTOOL.Checks`
   covers what is covered there; anything else wants a harness or the real app.

### The three-renderer rule

Every declaration exists three times over and all three have to agree, because a member signs the
first and compliance reads the third:

| | Insider | Related Party & COI |
|---|---|---|
| screen | `Components/Pages/SubmitInsiderDeclaration.razor` | `SubmitRelatedPartyCoiDeclaration.razor` |
| PDF | `Data/Governance/InsiderDeclarationPdfBuilder.cs` | `RelatedPartyCoiDeclarationPdfBuilder.cs` |
| report | `Data/Reports/InsiderDetailSheet.cs` | `Components/Pages/Admin/RpCoiDetailReport.razor.cs` |

Change what one shows and the other two are part of the change.

## Things that have already gone wrong this way

Each of these was found from a screenshot, not from the code:

- **Documents moved out of `wwwroot` into `dbo.DocumentBlobs`** and several call sites still asked
  for a `FilePath`. A member's Emirates ID was invisible to the document endpoint, to the preview,
  and to both declaration forms — one of which then refused to submit because the file it had just
  accepted "was missing". Anything touching a document asks `DocumentUrl.Exists(BlobId, FilePath)`,
  never a path alone.
- **`RelativeRelationship.Mother` is zero.** A row with `IsSelf` set leaves the relationship
  meaningless, so a renderer that printed it anyway had the declarant down as his own mother. Use
  `RelativeRelationships.HeldBy(isSelf, relationship)`.
- **How a relationship is named was written out twelve times** and the copies drifted. One
  definition now, in `RelativeRelationships`, and `src/CGTOOL.Checks` fails if a surface disagrees.
- **A field kept being saved after its input was removed.** `NumberOfSharesHeld` still round-trips
  but nothing captures it, so a report header read "Shares held 0" beside a grid declaring
  millions. If a report shows a constant, check whether anything still writes it.
- **An enum's `_ => value.ToString()` fallback looks deliberate on a page.** "FatherInLaw" is not
  a label; it is a missing one.
- **A report is described once and rendered several ways** (`ReportDocument`), and the renderings
  drift one at a time. When signatures moved into the database the PDF writer was taught to read
  the bytes and the on-screen viewer was left reading a path that is now null, so every signature
  since showed as a broken image on screen while the PDF of the same report drew it correctly.

- **A method that fills a form from a list takes the wrong row.** "Fill from directory" read the
  entity's whole employee list and used `records.FirstOrDefault()`, so it wrote the first
  employee's name, title, address and Entra object id onto whichever person was being edited. An
  error message hid it for months: the lookup also asked Graph to `$filter=companyName eq '...'`,
  which /users answers 400 for, so every click reported "No Azure AD records were returned for
  this entity" instead. Fixing the visible failure would have shipped the silent one.
- **An import that creates rows decides their defaults by omission.** Members imported from the
  spreadsheet carried every declaration flag false, so each one had nothing to declare, nothing to
  onboard and an empty My Workspace until an administrator opened the record and ticked boxes by
  hand. `MemberDefaults.ApplyToNew` is now the one answer, applied by every path that creates a
  member.
- **Two fields that describe the same fact are read by different screens.** `DeclarationType`
  shows on the member's record and in the Related Party master report; `IsBoardMember` decides
  which RP & COI form variant renders and who counts as board-or-executive in the Related Party
  Register. Setting only the first gave a person imported under the board entity a record saying
  "Board Member" while they were served the Executive Management form. When a change writes one
  of a pair, find who reads the other.

## Consistency checks

`src/CGTOOL.Checks` is a console program run by CI after the build. It holds whatever two places
have to agree about where nothing would otherwise notice if they stopped — it walks the built
assembly rather than a list someone maintains, so a surface added later is checked without being
registered anywhere. It is a console program and not a test framework because the build agent is
self-hosted, shares a disk the deploy pipeline notes is at 95%, and has no test packages cached.

When you fix a class of bug rather than an instance, add the check that would have caught it,
then break it on purpose once to confirm it goes red and names the offender.

## SQL lives in `scripts/`

Committed, so it can be pulled and run rather than pasted into chat. This repository is **public**:
never commit a real email address, connection string, key or password, including as a default in a
diagnostic script.

## Deploying

Pipeline → load the site once so `MigrateAsync` runs → re-run `scripts/stored-procedures.sql`.
Domain writes all go through `dbo.usp_<Entity>_<Action>` via `IStoredProcedureExecutor`, so a new
write path needs its procedure in that script as well as its C#.

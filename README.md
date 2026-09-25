# CGTOOL — DI Corporate Governance Tool

Trades and transactions monitoring for employee trading compliance, related-party
conflicts of interest, and transaction threshold controls, built from the
Corporate and Governance Tool design document (Admin Panel, User Management,
Control Panel) plus the follow-up User Management requirements (UM-01..UM-07:
Azure AD SSO, consolidated user management, role management, admin support
impersonation).

## Stack

- .NET 10 (LTS), Blazor Web App (Server interactivity)
- ASP.NET Core Identity (roles: `Administrator`, `ComplianceOfficer`, `Normal Staff`,
  plus any custom roles created from User Management)
- Login: Azure AD SSO (primary) or the corporate-directory form (secondary, for
  External Members and Development) — see **Authentication** below
- EF Core 10 + SQL Server (`Microsoft.EntityFrameworkCore.SqlServer`)

### Toolchain

The SDK is pinned in `global.json` to the 10.0.1xx band (`rollForward: latestFeature`), so every
machine and build agent compiles against the same major SDK. Install the .NET 10 SDK, or use
Visual Studio 2026, which ships with it. Visual Studio 2022 cannot build this project — it does not
carry a .NET 10 SDK.

## Project layout

```
src/CGTOOL.Web/
  Data/
    ApplicationDbContext.cs      Identity + governance DbContext
    Governance/                  Domain model, auth, audit log, scheduled activities
    Migrations/                  EF Core migrations
  Components/
    Pages/Dashboard.razor(.cs/.css)     Transaction monitoring dashboard
    Pages/Impersonate.razor            Act on behalf of an approved member (RPR/COI)
    Pages/Admin/UserManagement.razor(.cs)   Member list — search, active/inactive filter,
                                        pagination (25/page), role management, audit history
    Pages/Admin/MemberDetail.razor(.cs)     Add/edit a single member on its own page
    Pages/Admin/                       Companies, Departments, Declaration Setup,
                                        Schedule Activities, Audit Log
    Layout/IdleLockOverlay.razor            15-minute inactivity lock
    Layout/ImpersonationBanner.razor        Member-level "acting on behalf of" banner (RPR/COI)
    Layout/AdminImpersonationBanner.razor   Admin support "log in as" banner (UM-03)
```

## Authentication

Two ways to sign in, both shown on the login page:

- **Azure AD SSO (primary, UM-02)**: a "Sign in with Microsoft" button, registered
  as an external OpenID Connect login provider feeding into ASP.NET Core Identity's
  own cookie (so existing role-based `[Authorize]` checks are unaffected). Configure
  via `AzureAd:TenantId` / `AzureAd:ClientId` / `AzureAd:ClientSecret` (see below).
  Requires a real Azure AD app registration with a redirect URI of
  `https://<host>/signin-oidc-azuread` and the delegated `User.Read` Graph
  permission — only usable once configured against your real tenant.
- **Corporate-directory form (secondary)**: for External Members (no Azure AD
  account, per the design doc's `IsExternalMember` flag), and as the default
  login whenever LDAP isn't configured. Which check runs is decided by
  configuration, not `ASPNETCORE_ENVIRONMENT`: if `ActiveDirectory:Server` is
  set, it performs a real LDAP bind (`LdapActiveDirectoryAuthenticator`); if not,
  it checks the local ASP.NET Core Identity password hash instead
  (`LocalIdentityAuthenticator`) — so local login works out of the box in any
  environment until real LDAP is wired up. Register a test account at
  `/Account/Register`, then sign in at `/Account/Login`.

On first successful sign-in (either path) an `ApplicationUser` is auto-provisioned.

### UM-01: Azure AD employee lookup

The **User Management** screen's "Load from Azure AD" button
(`GraphDirectoryEmployeeProvider`) looks up an entity's employees via Microsoft
Graph using an app-only (client credentials) token — requires the **application**
`User.Read.All` Graph permission on the same app registration, granted admin
consent. Employees are matched to a Company by Azure AD's `companyName` user
attribute equalling the Company's Name. In Development (or if Azure AD isn't
configured), this falls back to manual entry, matching the design doc's "if the
entity AD is not synced" case.

## Configure the database connection

`appsettings.json` currently commits the real `UATWEB01` connection string
directly — a deliberate choice for this test server, made at the repo owner's
request. If you'd rather keep it out of source control on your own fork/clone,
use .NET user-secrets or an environment variable instead, which override the
value in `appsettings.json`:

```bash
cd src/CGTOOL.Web
dotnet user-secrets init
dotnet user-secrets set "ConnectionStrings:DefaultConnection" \
  "Server=UATWEB01;Database=CGS;User Id=cgtool;Password=cgtool;TrustServerCertificate=True;"
```

Or, for non-development environments, set the environment variable:

```bash
export ConnectionStrings__DefaultConnection="Server=UATWEB01;Database=CGS;User Id=cgtool;Password=cgtool;TrustServerCertificate=True;"
```

## Configure outbound email (welcome/declaration emails, scheduled reminders)

SMTP (the old internal-relay and Gmail options) is no longer used. The only
outbound mail path is **SQL Server Database Mail** (`SqlDbMailSender` /
`SqlDbMailActivityEmailSender` / `SqlDbMailMemberWelcomeEmailSender`), driven
by `Smtp:SqlDbMailProfile` (the Database Mail profile name configured on the
SQL Server instance itself, e.g. `"CGS"`). When that's not set, mail falls
back to logging only (`LoggingActivityEmailSender` /
`NoOpMemberWelcomeEmailSender`) so nothing crashes in an environment without
mail configured.

`Smtp:SqlDbMailProfile` is a plain (non-secret) setting since it's just a
profile name, not a credential — see `src/CGTOOL.Web/appsettings.json`.
Database Mail itself must be enabled and configured with that profile on the
target SQL Server instance (`sp_configure 'Database Mail XPs'`, then a mail
account + profile via Database Mail Configuration Wizard / `sysmail_*` procs)
— that's a SQL Server-side setup step, not something this app configures.

When configured, two things send real email:

- **New member welcome/declaration email**: sent automatically from
  `MemberDetail.razor.cs`'s `SaveAsync` whenever a brand new Member (one with
  an email address) is created. Branded HTML matching DI's navy/gold header,
  covering the Insider Declaration and Related Party & COI declaration, a
  link to `https://cg.dubaiinvestments.com/`, a Corporate Affairs Office
  contact line, and a light-gray confidentiality disclaimer footer (shared
  template: `WelcomeEmailTemplate`).
- **Scheduled activity reminders** (via `ScheduledActivityHostedService`).

> Database Mail is only reachable from the SQL Server instance itself, so it
> cannot be live-tested from this sandbox — verified by build only. Please
> test the actual send from within your VS/production environment once
> Database Mail is configured on the server.

### Insider Trading declaration confirmation email + PDF attachment

Submitting (or updating) an Insider Trading declaration sends a corporate-
branded HTML confirmation email (same DI navy/gold styling as the welcome
email above), built by `SubmitInsiderDeclaration.razor.cs`'s
`BuildConfirmationEmail`.

The declaration itself is rendered as a branded PDF (`InsiderDeclarationPdfBuilder`,
using `PDFsharp`) and attached via `SqlDbMailSender.SendWithFileAttachmentAsync`.
This needs `Smtp:DatabaseMailAttachmentFolder` set to a folder path that **both**
this app *and* the SQL Server instance running Database Mail can read/write —
`sp_send_dbmail`'s `@file_attachments` parameter is resolved by the SQL Server
engine itself, not by this app, so a local app-server path won't work unless
the app and SQL Server share a filesystem. In practice this is usually a UNC
share, e.g.:

```json
"Smtp": {
  "SqlDbMailProfile": "CGS",
  "DatabaseMailAttachmentFolder": "\\\\fileserver\\cgtool-mail-attachments"
}
```

Files are written with a random unique prefix and left in place after sending
(Database Mail queues sends asynchronously, so deleting the file immediately
after the app's call returns could race a send that hasn't happened yet) —
set up a periodic cleanup (SQL Agent job or scheduled task) to purge old files
from that folder. When `DatabaseMailAttachmentFolder` is left blank, the
confirmation email still sends with the branded HTML body, just without a PDF
attached — the email tells the declarant they can view/print their
declaration from **My Declarations** instead.

The PDF's text is rendered with the bundled DejaVu Sans font
(`wwwroot/fonts/DejaVuSans*.ttf`, Bitstream Vera license — freely
redistributable) rather than relying on fonts installed on the host OS, since
PDFsharp has no GDI to fall back on for font resolution on .NET
(`PdfFontResolver`, registered once via `GlobalFontSettings.FontResolver` in
`Program.cs`).

## Configure Insider Trading declaration ID document auto-capture (OCR)

The Insider Trading declaration wizard (`SubmitInsiderDeclaration.razor`) can
auto-fill the Emirates ID / Passport number, name, and expiry date fields from
the uploaded file using Azure AI Document Intelligence's `prebuilt-idDocument`
model (`DocumentIntelligenceService`). It's optional — when not configured,
`NotConfiguredDocumentIntelligenceService` is registered instead and the
declarant just types those fields in by hand, same as every other field on
the form.

Set these as user-secrets (never commit a real endpoint or key to
`appsettings.json` — the checked-in values are empty placeholders):

```bash
cd src/CGTOOL.Web
dotnet user-secrets set "DocumentIntelligence:Endpoint" "https://<your-resource-name>.cognitiveservices.azure.com"
dotnet user-secrets set "DocumentIntelligence:ApiKey" "<key from the Azure resource's Keys and Endpoint page>"
```

In production, set the same two keys via the App Service's Application
Settings, or better, reference them from Azure Key Vault — never as a literal
value checked into any file in this repo.

Every field Document Intelligence fills stays a normal, editable input, and
extraction failures (unreadable file, service down, not configured) just fall
back to manual entry rather than blocking the upload — OCR here is a
convenience, not a dependency. Its accuracy is generally strong on passports
and typical national IDs but has been observed to be less consistent
specifically on UAE Emirates ID cards (bilingual layout, `784-YYYY-NNNNNNN-C`
ID format); verify a few real cards after configuring it.

## Enable Change Data Capture (CDC)

`scripts/enable-cdc.sql` turns on SQL Server CDC for every master-data and
transactional table (Companies, Departments, Members,
MemberImpersonationApprovals, Transactions, AuditLogEntries,
DeclarationSetups, ScheduledActivities, ScheduledActivityDates, plus the
ASP.NET Core Identity tables), and configures the CDC cleanup job so captured
history is **never purged** (retention pushed out to 10 years and the
cleanup job itself disabled, belt-and-braces).

This is a standalone DBA-run script, **not** wired into the EF Core migration
pipeline — CDC needs sysadmin rights and a running SQL Server Agent, which
`dotnet ef database update` / the app's auto-migrate-on-startup shouldn't
depend on. Run it once per database:

```bash
sqlcmd -S UATWEB01 -d CGS -i scripts/enable-cdc.sql
```

It's safe to re-run: every step checks whether CDC is already enabled before
enabling it again.

For Production, also configure:

```bash
# LDAP fallback login (External Members / non-SSO accounts)
export ActiveDirectory__Server="dc01.di.corp.local"
export ActiveDirectory__Domain="DI"

# Azure AD SSO + Graph employee lookup (same app registration, needs both a
# delegated User.Read permission for sign-in and an application User.Read.All
# permission, with admin consent, for the directory lookup)
export AzureAd__TenantId="<tenant-guid>"
export AzureAd__ClientId="<app-registration-client-id>"
export AzureAd__ClientSecret="<client-secret>"
```

## Database writes go through stored procedures

Every insert/update/deactivate/delete this app performs on its own domain
tables (Companies, Departments, Members, MemberImpersonationApprovals,
Transactions, DeclarationSetups, DeclarationSubmissions, ScheduledActivities,
ScheduledActivityDates, AuditLogEntries) is executed via a SQL Server stored
procedure — see `scripts/stored-procedures.sql` for the full set
(`dbo.usp_<Entity>_<Action>`). The C# side never builds ad-hoc INSERT/UPDATE
SQL or calls `DbContext.SaveChangesAsync()` for these tables; instead each
entity has a small writer class (`Data/Governance/*Writer.cs`, e.g.
`MemberWriter`, `CompanyWriter`) that calls its procedure through
`IStoredProcedureExecutor` (`StoredProcedureExecutor.cs`), a thin ADO.NET
wrapper that runs `SqlCommand`s (`CommandType.StoredProcedure`) on the same
connection/transaction as the current `DbContext`. Uniqueness and
referential-integrity checks that used to be caught as a `DbUpdateException`
(duplicate `Company.ShortCode`/`Department.Code`, a login already linked to
another Member, a reporting-manager cycle, a duplicate declaration
submission, a duplicate scheduled-activity trigger date) are now enforced
inside the procedure itself via `THROW` with a custom error number
(50001–50007, documented at the top of the script); the C# call sites catch
`Microsoft.Data.SqlClient.SqlException` and switch on `.Number` to show the
same toast messages as before.

Two things are deliberately **out of scope** and still go through EF Core:
- **ASP.NET Core Identity's own tables/APIs** (`AspNetUsers`/`AspNetRoles`/
  `AspNetUserRoles` — login, lockout, password hashing, role assignment via
  `UserManager`/`RoleManager`/`SignInManager`). Identity owns that storage;
  replacing its internal data access is a separate, security-sensitive
  undertaking with little practical benefit.
- **`GovernanceSeeder`**, the one-time development seed data inserted on
  first run (see **Run** below) — it's startup fixture data, not a runtime
  write path.

All reads (search, filters, pagination, the dashboard, audit log browsing)
are unaffected and remain LINQ/EF Core.

Deploy the procedures once per database (safe to re-run — every procedure
uses `CREATE OR ALTER`):

```bash
sqlcmd -S UATWEB01 -d CGS -i scripts/stored-procedures.sql
```

**Migrations first, then this script.** The procedures name columns, and SQL Server resolves
column names when a procedure is created — unlike table names, which it resolves lazily. Deploy
against a database whose schema is behind the application and you get a run of
`Invalid column name` errors, one per column per procedure, while every other procedure in the
file deploys happily: a database that looks deployed and is quietly missing the procedures that
matter. So add the columns first — start the app once against the database (it calls
`Database.Migrate` at startup) or run `dotnet ef database update` — and then run the script.

The script guards against this itself. If the schema is behind, or `-d` pointed at the wrong
database, it says so once and deploys nothing at all rather than leaving a half-applied set.
The guard checks the newest columns the file depends on, so **extend it when a migration adds a
column a procedure writes to** — otherwise the next person to deploy in the wrong order gets the
old confusing failure back.

The script begins by setting `ANSI_NULLS` and `QUOTED_IDENTIFIER` ON, and it has to. SQL
Server captures those settings with each procedure at creation time, and a procedure created
with `QUOTED_IDENTIFIER OFF` fails at runtime on any table carrying a filtered index —
`Members` has several — with:

```
INSERT failed because the following SET options have incorrect settings: 'QUOTED_IDENTIFIER'
```

SSMS connects with it ON, but **sqlcmd defaults it OFF**, so running this file without those
SET statements produced procedures that looked deployed and then failed the first time anyone
saved a user. Do not remove them, and do not split the file in a way that leaves a
`CREATE OR ALTER` in a session that has not run them.

The file deploys into whichever database the connection is on, so `-d` above is load-bearing.
It used to carry a hardcoded `USE [CGS]`, which ignored `-d` and sent every deploy to that one
database name regardless of what was asked for; that is gone. In SSMS, pick the database first.

## Run

```bash
cd src/CGTOOL.Web
dotnet run
```

On first run the app applies EF Core migrations automatically and creates the
`Administrator` / `ComplianceOfficer` / `Normal Staff` roles and the setup account.
It creates **no business records** — no companies, departments, members or
transactions. The sample data described under **Demo data** below is off unless
`Seed:DemoData` is turned on.

## The debugger stops on `NavigationException`

Signing in under the Visual Studio debugger raises:

```
Exception User-Unhandled
Microsoft.AspNetCore.Components.NavigationException
```

on `navigationManager.NavigateTo(uri)` in `IdentityRedirectManager`. **Nothing is
wrong** — press Continue (F5) and the redirect completes. Blazor's static server
rendering signals a redirect by throwing this exception and catching it in the
framework; `IdentityRedirectManager` deliberately lets it pass through, so under
Just My Code the debugger reports it as escaping user code, on every sign-in,
sign-out and Manage redirect.

To stop the prompt, once per machine:

1. **Debug → Windows → Exception Settings** (`Ctrl+Alt+E`).
2. Expand **Common Language Runtime Exceptions** and search for
   `NavigationException`. If it isn't listed, click **+**, pick that category and
   enter `Microsoft.AspNetCore.Components.NavigationException`.
3. Leave its **Break When Thrown** box *unchecked*.
4. Right-click it and check **Continue When Unhandled in User Code**.

That setting lives in the per-developer `.suo`, so it is not something the
repository can carry — each developer sets it once.

There is no code-level fix. `[DebuggerDisableUserUnhandledExceptions]` reads as
though it should work and does not: it suppresses the break only for an exception
the attributed method *catches*, and this one passes straight through. It belongs
on the framework method that catches it, which is not ours to annotate.

## First-time setup

1. Sign in with the setup account, or with SSO once users have been imported
   (see **First sign-in, and where users come from** below).
2. From **User Management**, grant `ComplianceOfficer`/`Administrator` to
   teammates, create any additional custom roles you need, and open each
   Member's edit page to link their login account and review their audit
   history. Creating the first real administrator retires the setup account.

## Features by requirement

| Requirement | Implementation |
|---|---|
| User classification (Admin / Normal User) | `Administrator`, `ComplianceOfficer`, `Normal Staff` roles |
| User Management (Add/Edit/Deactivate user, entity, AD load, role flags, external member, impersonation) | `/admin/user-management` (list, search, filter) + `/admin/user-management/member/{id}` (add/edit) |
| Users list: pagination, Active/Recent/Inactive tabs | `/admin/user-management` — 25 users per page, Active/Recent/Inactive tabs (Active selected by default); Recent shows users created or modified within a selectable window (7/30 days — the FRD leaves the exact window open, "TBC with Corporate Affairs"); reporting-manager and approved-impersonator pickers only offer active users |
| Role assigned (per user) | Insider Trading, Conflict of Interest, Related Party Register, Related Party Transaction — checkboxes on `/admin/user-management/member/{id}` |
| Profile picture | Shown top-left of the nav (username + picture, falls back to initials); uploaded from the Login account section of `/admin/user-management/member/{id}` (JPEG/PNG/WebP, up to 2 MB) |
| Maintain change log | `/admin/audit-log` (global) + per-user history on `/admin/user-management/member/{id}` (UM-06) |
| New-user notification prompt (FRD §2.2) | On saving a new user (with an email), `NotifyUserPrompt` asks "Do you want to notify user?" — **Notify Now** sends immediately, **Send Message Later** queues it on `/admin/pending-notifications` for an admin to trigger, **No** does neither; every outcome is recorded as a `MemberNotification` (Pending/Sent/Failed, with timestamps) and an `AuditAction.Notify` entry |
| Notification email template (FRD §2.3) | `WelcomeEmailTemplate` (shared by `MemberWelcomeEmailSender` / `InternalSmtpMemberWelcomeEmailSender`) — subject/body follow Corporate Affairs' confirmed template (declarations link, Corporate Affairs contact, confidentiality disclaimer) |
| CDC on master/transactional data, never purged | `scripts/enable-cdc.sql` (see **Enable Change Data Capture** above) |
| All database writes via stored procedures | `scripts/stored-procedures.sql` + `Data/Governance/*Writer.cs`/`StoredProcedureExecutor.cs` (see **Database writes go through stored procedures** above) |
| Insider Declaration / Related Party &amp; COI declaration submission | `/my-declarations` — every signed-in user submits their own declarations for each open `DeclarationSetup` period; one submission per user per period (unique index) |
| Normal Staff role restricted to declarations only | `GovernanceRoles.IsNormalStaffOnly` — hides Dashboard/Impersonate from nav and redirects direct navigation to `/my-declarations` for users whose only role is Normal Staff |
| Sign out / 15-minute lock | `IdleLockOverlay` — locks without ending the session; unlock re-verifies the password |
| Switch user (general) | Nav "Switch User" signs out to the login page |
| UM-01 Azure AD employee lookup | `GraphDirectoryEmployeeProvider` (see above) |
| UM-02 Azure AD SSO | OpenID Connect external login provider (see **Authentication**) |
| UM-03 Admin support impersonation | "Log in as this user" on `/admin/user-management/member/{id}` (Administrator only), `AdminImpersonationBanner`, fully audit logged |
| UM-04/05 Consolidated User Management screen | Member list (`/admin/user-management`) + a dedicated edit page (`/admin/user-management/member/{id}`) combining login linking, role assignment, and audit history in one place per member |
| UM-06 Per-user audit history | `<details>` panel on `/admin/user-management/member/{id}`, filtered to that Member + linked login account |
| UM-07 Role management | Create/delete custom roles from `/admin/user-management` (built-in roles and roles still assigned to users can't be deleted) |
| Company / Departments / Job Title | `/admin/companies`, `/admin/departments`, `/admin/job-titles` |
| Declarations Setup (31-Jul-2026 requirement note) | `/admin/declarations-setup/{insider-trading\|conflict-of-interest\|related-party-register\|blackout-periods\|scheduled-maintenance}` — per-type config (Frequency, Due date offset in days, No. of reminders, Reminder frequency, Email template) plus a "Send Now" action that emails every active user of a chosen entity (or all entities) using the configured template, records a `DeclarationCycleRun`, and logs an audit entry with the recipient count/entity/due date. `DeclarationCycleReminderHostedService` sends the configured number of reminders at the configured cadence (Daily/Weekly) after each run — only the first notification is admin-initiated, reminders are automatic. Every config save also logs an audit entry listing exactly which fields changed (old → new). Supersedes the old Setup > Activities menu (declaration-setup/schedule-activities pages still work by direct URL but are no longer linked in the nav). |
| Company / Departments / Job Title / (legacy) Declaration Setup / Schedule Activities | Legacy config pages, no longer in the nav: `/admin/declaration-setup`, `/admin/schedule-activities`, `ScheduledActivityHostedService` |
| Grouped nav, top-level order: Dashboard &gt; My Account &gt; Admin Setup &gt; Data Management &gt; Declaration Setup | `NavMenu.razor` — collapsible groups (no JS, plain component state). My Account: Profile (merged Profile+Email) and Security (renamed from Password); Personal Data removed. Data Management: Company/Departments/Job Title, Audit Logs (`/admin/audit-log` today-only vs `/admin/audit-log/periodic` editable range). Declaration Setup is now its own top-level group (not nested under Data Management) with the 5 sub-items (see Declarations Setup above). Pending Notifications, and the old Activities > Declaration / Event & Notification branch, were removed from the menu (pages still reachable by direct URL). Switch User/Logout are now icon-only buttons directly under the profile picture instead of text rows at the bottom of the nav; the profile block shows "{FullName} \* {Job title}" (from the linked Member) instead of the raw login name/email. |
| Weekly declaration reminders until due date (legacy) | `DeclarationReminderHostedService` — for each open Insider/COI `DeclarationSetup`, emails everyone who hasn't submitted yet, once every 7 days, until the period's due date. No longer linked from the nav; superseded by Declarations Setup above. |
| All errors shown as toast notifications | `ToastService` + `ToastContainer` (interactive pages); ASP.NET Core Identity's own static-SSR pages (Login, Register, Manage — these can't host a live circuit, since cookie sign-in needs the raw HTTP response) restyle their existing status banners to look/behave the same way |
| Field validation (email format, required fields, referential integrity) | `[EmailAddress]`/`[Required]`/`[Range]` on `Member`/`Company`/`Department`, enforced via `EditForm`+`DataAnnotationsValidator` (or `Validator.TryValidateObject` for the table-based Department/Declaration Setup/Schedule Activities forms, which can't host an `<EditForm>` inside a `<table>`). Referential integrity: a filtered unique index stops one login account from being linked to two Members, a unique index stops duplicate (Month, Day) trigger dates on the same scheduled activity, and an app-level check blocks a reporting-manager assignment that would create a cycle |
| Audit log: WHO/WHY/HOW/WHEN + justification | `AuditLogEntry`: `ActorDisplayName` (who), `OccurredAtUtc` (when), `Details` (how), `Justification` (why — required via `JustificationPrompt` for every deactivate/delete/role-delete) |
| No hard deletes — disable/inactivate instead | Company and Department now have an `Active` flag with Deactivate/Reactivate (matching Member's existing pattern) instead of a delete button; deactivating a Member also locks their linked login via ASP.NET Core Identity lockout, actually blocking sign-in (checked explicitly in the corporate-directory login flow; Azure AD SSO respects it automatically) |
| Capture IP, username, trace log | `ClientContext` captures the circuit's IP/user agent once (via the `HttpContext` cascading parameter) and `AuditLogger` stamps every entry with it; username is `ActorDisplayName` |

## Verifying without a live database or Azure tenant

`UATWEB01`, the corporate LDAP, and Azure AD are only reachable from the
corporate network / your real tenant, so this environment can't run the app
end-to-end. The domain model, seed data, auth logic, scheduled-activity
trigger logic, role-creation/deletion guards, and per-member audit filtering
were all verified with a throwaway SQLite-backed console harness (not part of
this repo) referencing this project directly; `dotnet build` and
`dotnet ef migrations script` (which generates SQL without needing
connectivity) both pass cleanly, and the app was smoke-tested to confirm it
fails only at the database-connection step (no DI/startup errors), including
with dummy Azure AD config values set to exercise that registration path.

The `scripts/stored-procedures.sql` T-SQL (see **Database writes go through
stored procedures** above) could not be exercised this way — stored
procedures are a SQL Server-only construct with no SQLite equivalent, so
that harness can no longer simulate the app's write paths. Each procedure
was instead checked by hand against the exact column names/types/indexes in
`Data/Migrations/ApplicationDbContextModelSnapshot.cs`, and each C# writer's
parameter list was checked against its procedure's signature; the SQL itself
has not run against a live SQL Server engine. Run it against a real UAT/dev
database and exercise Save/Add/Deactivate on each admin screen before
trusting it in production.

## First sign-in, and where users come from

People sign in with **Entra ID single sign-on**. There is no self-registration — the register
screens were removed — and the member directory is loaded from Entra rather than typed in.

### The setup account

A brand new deployment has no accounts at all, so nobody could sign in to load any. Startup
provisions a single bootstrap administrator for exactly that gap
(`Data/Governance/DefaultAdminProvisioner.cs`), and the sign-in page offers its password form while
the deployment has no real users. Once real users exist the page signs in with Entra instead.

```json
"DefaultAdmin": {
  "Enabled": true,
  "UserName": "admin@cgtool.local",
  "Password": ""
}
```

Leave `Password` empty and a documented fallback (`Admin@12345`) is used and logged as a warning —
fine for a first run on a closed network, not for anything reachable. Set a real one through
user-secrets, an environment variable or Key Vault before the deployment is exposed:

```bash
dotnet user-secrets set "DefaultAdmin:Password" "<a real password>"
```

`DefaultAdmin:Password` is authoritative every time the app starts, not only when the account is
first created. Change it and restart, and the account is reset to the new value (logged as a
warning). This matters because the alternative is silent: edit the setting on a deployment that
already has the account, and without this the documented password simply would not work, which
looks exactly like a broken login rather than a setting that was ignored. The corollary is that
changing this account's password from inside the app does not survive a restart — which is the
right trade for a bootstrap account that retires itself.

The rule that governs the account is a single invariant, applied at startup and again whenever the
Administrator role is granted: **it is enabled only while the deployment has no other
administrator.** So it retires itself the moment a real administrator exists, and comes back if
every real administrator is later removed — otherwise losing the last administrator would lock the
deployment out entirely. While it is in use, a banner across every screen says so.

`DefaultAdmin:Enabled: false` turns the whole mechanism off for a deployment that provisions its
first administrator some other way.

### Loading users from Entra ID

**User Management → Sync from Entra ID** reads every user in the tenant through Microsoft Graph and
writes them into the member directory (`Data/Governance/EntraDirectorySync.cs`), capturing the
profile fields the governance screens display:

| Entra ID attribute | Becomes |
|---|---|
| `displayName` | Member full name |
| `mail` (or `userPrincipalName`) | Email, and the login account's user name |
| `companyName` | Entity — matched to a Company by name, created if new |
| `department` | Department — matched by name, created if new |
| `jobTitle` | Designation |
| `/photo/$value` | Profile picture under `wwwroot/uploads/profile-pictures` |
| `accountEnabled` | Active, and the login account's lockout |

It is a one-way import: Entra is the system of record for who exists and what their profile says, so
those fields are refreshed on every run. Everything the governance process owns — declaration access
flags, RP Transaction role, reporting manager, impersonation approvals — is left exactly as an
administrator set it. A user with no `companyName` is reported rather than guessed at, since every
member has to belong to an entity.

It needs the same app registration as SSO, with the **application** `User.Read.All` Graph permission
and admin consent. Writes go through the stored-procedure writers like every other write here.

### Configuring Entra ID

The settings live in `appsettings.json`, alongside the other deployment settings:

```json
"AzureAd": {
  "Instance": "https://login.microsoftonline.com/",
  "TenantId": "<directory (tenant) id>",
  "ClientId": "<application (client) id>",
  "ClientSecret": "<client secret value>"
}
```

Single sign-on is registered only when **both** `TenantId` and `ClientId` are filled in; the
directory sync additionally needs `ClientSecret`. `Instance` only changes for a sovereign cloud.

In the app registration, add a **Web** redirect URI ending in `/signin-oidc-azuread` — the callback
path is fixed in `Program.cs`. For a developer machine running the `https` launch profile that is:

```
https://localhost:7184/signin-oidc-azuread
```

Two different permissions are needed, and they are easy to confuse: **delegated** `User.Read` for
signing in, and **application** `User.Read.All`, with admin consent, for the directory sync.

Anything set here can still be overridden per machine or per environment without editing the file —
`dotnet user-secrets set "AzureAd:ClientSecret" "…"` on a developer machine, or the environment
variable `AzureAd__ClientSecret` in a deployment. That is worth doing for the client secret in
particular, since a value in `appsettings.json` is committed to the repository and readable by
anyone who can read it; rotating it then means a commit rather than a setting change.

> Entra ID is only reachable from the organization's real tenant, so the sync has been verified by
> build and by review against the Graph API contract, not against a live directory. Run it once
> against the real tenant and check the first few members before relying on it.

### Demo data

`Seed:DemoData` (default **false**) controls `GovernanceSeeder`, which invents sample entities,
departments, members and transactions. It is off so that a real database is never populated with
people who do not exist; turn it on only for a demonstration environment.

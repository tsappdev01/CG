#!/usr/bin/env python3
"""Writes the four declaration notification e-mail templates.

They are near-identical -- the same masthead, rule, footer and confidentiality notice -- and
differ only in what the recipient is being asked to do. Four hand-maintained copies drift: the
re-brand that produced them had to change the same five colours in each place it appeared, and
the next one would too. So the chrome lives here once and the wording per declaration is data.

Regenerate rather than editing the HTML:      python3 scripts/templates/build-notification-emails.py

Placeholders are filled by DeclarationCycleReminderHostedService.ApplyPlaceholders:
  {FullName}  the recipient
  {DueDate}   the due date set on the Send Notification tab
  {LogoUrl}   absolute when sent, site-relative in the setup screen's preview
  {BaseUrl}   likewise, so a link is never tied to one host

Every link goes to /Account/Login carrying a ReturnUrl. Recipients are nearly always signed out
when they open one of these, so a bare deep link only bounces them through the login page anyway
-- and lands them on the dashboard afterwards rather than on the thing the mail asked for. Going
to the login page explicitly, with where they were headed attached, gets them to the right screen.
"""
import pathlib
import urllib.parse

NAVY, GOLD, GOLD_INK, INK, MUTED, FAINT, LINE, LINK = (
    "#002850", "#C8952E", "#0D2B57", "#16202E", "#64748B", "#94A3B8", "#e6ebf2", "#1A4F86")
RULE = "#C79A3C"

# key -> (title, the paragraph that says what is being asked, button label, button path)
DECLARATIONS = {
    "insider-trading": (
        "Insider Declaration",
        "In line with the Corporate Governance Framework, you are required to submit your "
        "quarterly Insider Declaration pertaining to the previous quarter before "
        "<strong>{DueDate}</strong>.",
        "Submit Insider Declaration", "/declarations/insider-trading"),
    "conflict-of-interest": (
        "Conflict of Interest Declaration",
        "In line with the Corporate Governance Framework, you are required to submit your Related "
        "Party and Conflict of Interest declaration before <strong>{DueDate}</strong>.",
        "Submit Declaration", "/declarations/related-party-coi"),
    "related-party-register": (
        "Related Party Register",
        "In line with the Corporate Governance Framework, you are required to review your Related "
        "Party Register and confirm that it is complete and up to date before "
        "<strong>{DueDate}</strong>. Please add or amend any relatives, companies you hold an "
        "interest in, and shareholdings that have changed.",
        "Review My Register", "/my-register"),
    # Not a submission: there is no blackout form to fill in. It tells people a restriction is in
    # force, and the button goes to the register so they can see who else it covers.
    "blackout-periods": (
        "Blackout Period",
        "A blackout period is now in effect. Until <strong>{DueDate}</strong> you may not deal in "
        "the company&rsquo;s securities, either directly or through a related party. This applies "
        "to you and to the related parties recorded against you in the Corporate Governance Tool.",
        "Review My Register", "/my-register"),
}

SHELL = """<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8">
<meta name="viewport" content="width=device-width, initial-scale=1.0">
<title>{title}</title>
</head>
<body style="margin:0; padding:0; background-color:#f2f4f7; font-family:Calibri, Arial, sans-serif;">

  <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background-color:#f2f4f7; padding:24px 0;">
    <tr>
      <td align="center">

        <table role="presentation" width="600" cellpadding="0" cellspacing="0" style="background-color:#ffffff; border-radius:4px; overflow:hidden; box-shadow:0 1px 3px rgba(0,0,0,0.08);">

          <!-- Masthead. di-logo.jpg, not either PNG: e-mail clients are unreliable with PNG alpha,
               and that file is already flattened onto this exact navy. -->
          <tr>
            <td style="background-color:{navy}; padding:22px 32px;">
              <img src="{{LogoUrl}}" alt="{{ParentHoldingCompany}}" height="38" style="display:block; border:0; outline:none; text-decoration:none;" />
            </td>
          </tr>
          <!-- The gold rule, as on the report PDFs. font-size and line-height have to be set to the
               height as well: a cell holding only &nbsp; collapses to nothing in Outlook. -->
          <tr>
            <td height="3" style="background-color:{rule}; height:3px; font-size:3px; line-height:3px;">&nbsp;</td>
          </tr>

          <tr>
            <td style="padding:32px;">
              <p style="margin:0 0 16px 0; font-size:15px; color:{ink}; line-height:1.6;">
                Dear {{FullName}},
              </p>

              <p style="margin:0 0 16px 0; font-size:15px; color:{ink}; line-height:1.6;">
                {intro}
              </p>

              <p style="margin:0 0 28px 0; font-size:15px; color:{ink}; line-height:1.6;">
                {lead_in}
              </p>

              <table role="presentation" cellpadding="0" cellspacing="0">
                <tr>
                  <td align="center" style="border-radius:4px; background-color:{gold};">
                    <a href="{{BaseUrl}}{path}"
                       target="_blank"
                       style="display:inline-block; padding:12px 28px; font-size:14px; font-weight:bold; color:{gold_ink}; text-decoration:none; border-radius:4px;">
                      {button}
                    </a>
                  </td>
                </tr>
              </table>

              <p style="margin:28px 0 0 0; font-size:13px; color:{muted}; line-height:1.6;">
                If the button above does not work, copy and paste this link into your browser:<br>
                <a href="{{BaseUrl}}{path}" style="color:{link};">{{BaseUrl}}{path}</a>
              </p>

              <p style="margin:24px 0 0 0; font-size:15px; color:{ink}; line-height:1.6;">
                Should you have any queries on this matter, please contact the Corporate Affairs Office.
              </p>

              <p style="margin:20px 0 0 0; font-size:15px; color:{ink}; line-height:1.6;">
                Thank you.
              </p>
            </td>
          </tr>

          <tr>
            <td style="background-color:#f7f9fc; padding:20px 32px; border-top:1px solid {line};">
              <p style="margin:0; font-size:11px; color:{faint}; line-height:1.6;">
                This is an automated notification from the {{ParentHoldingCompany}} Corporate Governance Tool.
                Please do not reply to this email. The information contained in this message may be
                CONFIDENTIAL and is intended only for the named recipient. If you are not the intended
                recipient, please notify the sender and delete this message.
              </p>
            </td>
          </tr>

        </table>

      </td>
    </tr>
  </table>

</body>
</html>
"""

# DeclarationCycleType, in declaration order. The procedures take the int.
TYPE_IDS = {"insider-trading": 0, "conflict-of-interest": 1, "related-party-register": 2,
            "blackout-periods": 3}

# Used only where the row's subject is still blank, so a subject someone wrote is never replaced.
DEFAULT_SUBJECTS = {
    "insider-trading": "Submit your Insider Declaration",
    "conflict-of-interest": "Submit your Related Party & Conflict of Interest declaration",
    "related-party-register": "Review your Related Party Register",
    "blackout-periods": "Blackout period in effect",
}


def sql_chunks(html, size=3500):
    """A T-SQL literal is nvarchar(4000); a longer template silently truncates. Build it by
    appending chunks onto a variable that was CAST to nvarchar(max) first, splitting on line
    boundaries so the script stays readable."""
    out, current = [], ""
    for line in html.splitlines(keepends=True):
        if len(current) + len(line) > size and current:
            out.append(current)
            current = ""
        current += line
    if current:
        out.append(current)
    return out


def write_sql(here, bodies):
    lines = ["""/*
    ============================================================================================
    Writes the four declaration notification templates into DeclarationCycleSetups.EmailBody.

    GENERATED by scripts/templates/build-notification-emails.py -- do not edit. Change the
    templates there and re-run it, or the SQL and the HTML drift apart.

    Through dbo.usp_DeclarationCycleSetup_Update, not an UPDATE statement, because that is how the
    app writes this row and a second path to the same column is a second thing to keep correct.
    That procedure takes every column, so the reminder count, day and time are read first and
    passed back unchanged -- only the body moves. The subject is replaced only where it is still
    blank, so one someone wrote is never overwritten.

    A type with no settings row yet (a declaration whose screen has never been opened) is created
    by usp_DeclarationCycleSetup_EnsureExists first.

    DRY RUN BY DEFAULT. @DryRun = 1 reports what each row would become and rolls back. Set it to 0
    to commit. Re-runnable either way.
    ============================================================================================
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @DryRun bit = 1;

DECLARE @Id int, @Body nvarchar(max), @Subject nvarchar(200),
        @ReminderCount int, @ReminderDayOfWeek int, @ReminderTimeOfDay time(0);

BEGIN TRANSACTION;
"""]
    for key, html in bodies.items():
        type_id = TYPE_IDS[key]
        lines.append(f"\n/* ---- {key} (Type = {type_id}) ---- */\n")
        lines.append(f"EXEC dbo.usp_DeclarationCycleSetup_EnsureExists @Type = {type_id}, @NewId = @Id OUTPUT;\n")
        lines.append("SELECT @ReminderCount = ReminderCount, @ReminderDayOfWeek = ReminderDayOfWeek,\n"
                     "       @ReminderTimeOfDay = ReminderTimeOfDay, @Subject = EmailSubject\n"
                     "FROM   dbo.DeclarationCycleSetups WHERE Id = @Id;\n")
        subject = DEFAULT_SUBJECTS[key].replace("'", "''")
        lines.append(f"IF @Subject IS NULL OR LTRIM(RTRIM(@Subject)) = N'' SET @Subject = N'{subject}';\n")
        chunks = sql_chunks(html)
        for i, chunk in enumerate(chunks):
            escaped = chunk.replace("'", "''")
            if i == 0:
                lines.append(f"SET @Body = CAST(N'{escaped}' AS nvarchar(max));\n")
            else:
                lines.append(f"SET @Body = @Body + N'{escaped}';\n")
        lines.append("EXEC dbo.usp_DeclarationCycleSetup_Update @Id = @Id, @ReminderCount = @ReminderCount,\n"
                     "     @ReminderDayOfWeek = @ReminderDayOfWeek, @ReminderTimeOfDay = @ReminderTimeOfDay,\n"
                     "     @EmailSubject = @Subject, @EmailBody = @Body;\n")

    lines.append("""
/* ---- what the four rows now hold ---- */
SELECT Type,
       CASE Type WHEN 0 THEN N'Insider Trading' WHEN 1 THEN N'Conflict of Interest'
                 WHEN 2 THEN N'Related Party Register' WHEN 3 THEN N'Blackout Periods'
                 ELSE N'(other)' END AS Declaration,
       EmailSubject,
       LEN(EmailBody) AS BodyLength,
       CASE WHEN EmailBody LIKE N'%{LogoUrl}%' THEN N'yes' ELSE N'NO -- not the new template' END AS HasLogo,
       CASE WHEN EmailBody LIKE N'%{BaseUrl}%' THEN N'yes' ELSE N'NO -- link is tied to one host' END AS HasBaseUrl
FROM   dbo.DeclarationCycleSetups
WHERE  Type IN (0, 1, 2, 3)
ORDER  BY Type;

IF @DryRun = 1
BEGIN
    ROLLBACK TRANSACTION;
    PRINT N'DRY RUN -- rolled back. Set @DryRun = 0 to commit.';
END
ELSE
BEGIN
    COMMIT TRANSACTION;
    PRINT N'COMMITTED -- all four templates updated.';
END;
""")
    out = here.parent / "update-notification-templates.sql"
    out.write_text("".join(lines), encoding="utf-8")
    print(f"wrote ../{out.name}")


here = pathlib.Path(__file__).parent
generated = {}
for key, (title, intro, button, path) in DECLARATIONS.items():
    lead_in = ("You can review your register using the button below:"
               if key == "blackout-periods"
               else "Please submit your declaration using the button below:"
               if key != "related-party-register"
               else "Please review your register using the button below:")
    link = "/Account/Login?ReturnUrl=" + urllib.parse.quote(path, safe="")
    out = here / f"notification-{key}.html"
    out.write_text(SHELL.format(
        title=title, intro=intro, lead_in=lead_in, button=button, path=link,
        navy=NAVY, rule=RULE, gold=GOLD, gold_ink=GOLD_INK, ink=INK,
        muted=MUTED, faint=FAINT, line=LINE, link=LINK), encoding="utf-8")
    generated[key] = out.read_text(encoding="utf-8")
    print(f"wrote {out.name}")

write_sql(here, generated)

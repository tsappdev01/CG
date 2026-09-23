using Microsoft.EntityFrameworkCore;

namespace CGTOOL.Web.Data.Governance;

/// <summary>
/// Demonstration fixture data -- invented entities, departments, members and transactions used to
/// show the product with something on the screen. It is NOT reference data and NOT part of a normal
/// deployment: Program.cs only calls this when <c>Seed:DemoData</c> is explicitly turned on, so a
/// real database is never populated with people who do not exist.
/// </summary>
public static class GovernanceSeeder
{
    public static async Task SeedAsync(ApplicationDbContext db)
    {
        if (await db.Members.AnyAsync()) return;

        var di = new Company { Name = "Dubai Investments", ShortCode = "DI", City = "Dubai", Country = "United Arab Emirates" };
        var dip = new Company { Name = "Dubai Investments Park", ShortCode = "DIP", City = "Dubai", Country = "United Arab Emirates" };
        db.Companies.AddRange(di, dip);

        var corpDev = new Department { Code = "CORPDEV", Name = "Corporate Development" };
        var boardOffice = new Department { Code = "BOARD", Name = "Board Office" };
        var tradingDesk = new Department { Code = "TRADING", Name = "Trading Desk" };
        var finance = new Department { Code = "FIN", Name = "Finance" };
        var strategicPartnerships = new Department { Code = "STRATPART", Name = "Strategic Partnerships" };
        var treasury = new Department { Code = "TREASURY", Name = "Treasury" };
        var legalCompliance = new Department { Code = "LEGAL", Name = "Legal & Compliance" };
        db.Departments.AddRange(corpDev, boardOffice, tradingDesk, finance, strategicPartnerships, treasury, legalCompliance);

        var whitfield = new Member { Company = di, Department = finance, FullName = "T. Whitfield", JobTitle = "CFO", Email = "t.whitfield@di.ae", ConflictOfInterestAccess = true, InsiderTradingAccess = true, RelatedPartyTransactionAccess = true };
        var alvarez = new Member { Company = di, Department = corpDev, FullName = "M. Alvarez", JobTitle = "VP, Corporate Development", Email = "m.alvarez@di.ae", ConflictOfInterestAccess = true, InsiderTradingAccess = true, RelatedPartyTransactionAccess = true, ReportingManager = whitfield };
        var chen = new Member { Company = di, Department = boardOffice, FullName = "R. Chen", JobTitle = "Board Member", Email = "r.chen@di.ae", IsBoardMember = true, RelatedPartyRegisterAccess = true, ConflictOfInterestAccess = true, CanBeImpersonated = true };
        var okafor = new Member { Company = di, Department = tradingDesk, FullName = "J. Okafor", JobTitle = "Senior Analyst, Trading Desk", Email = "j.okafor@di.ae", InsiderTradingAccess = true, RelatedPartyTransactionAccess = true, ReportingManager = whitfield };
        var dubois = new Member { Company = dip, Department = strategicPartnerships, FullName = "S. Dubois", JobTitle = "Director, Strategic Partnerships", Email = "s.dubois@dip.ae", ConflictOfInterestAccess = true, ReportingManager = whitfield };
        var kowalski = new Member { Company = di, Department = treasury, FullName = "D. Kowalski", JobTitle = "Head of Treasury", Email = "d.kowalski@di.ae", InsiderTradingAccess = true, RelatedPartyTransactionAccess = true, ReportingManager = whitfield };
        var nakamura = new Member { Company = di, Department = boardOffice, FullName = "P. Nakamura", JobTitle = "Board Member", Email = "p.nakamura@di.ae", IsBoardMember = true, RelatedPartyRegisterAccess = true, ConflictOfInterestAccess = true, CanBeImpersonated = true };
        var marlowe = new Member { Company = di, Department = legalCompliance, FullName = "E. Marlowe", JobTitle = "General Counsel", Email = "e.marlowe@di.ae", RelatedPartyRegisterAccess = true, ConflictOfInterestAccess = true, InsiderTradingAccess = true, RelatedPartyTransactionAccess = true };

        db.Members.AddRange(whitfield, alvarez, chen, okafor, dubois, kowalski, nakamura, marlowe);

        // Board members can be impersonated by the General Counsel for their Related Party Register / COI filings.
        db.MemberImpersonationApprovals.AddRange(
            new MemberImpersonationApproval { Member = chen, Impersonator = marlowe },
            new MemberImpersonationApproval { Member = nakamura, Impersonator = marlowe });

        var t = new List<Transaction>
        {
            New("TX-3311", alvarez, "ATLASCO", TransactionSide.Sell, 4200, 118400, "2026-07-25", TransactionCategory.BlackoutPeriod, TransactionStatus.Escalated,
                "Sale executed 3 days into the Q3 earnings blackout window. No pre-clearance request on file. Escalated to General Counsel for disciplinary review under Section 4.2 of the Insider Trading Policy.",
                "ITP §4.2 — Blackout Period", "K. Osei, Compliance", "2026-07-25"),
            New("TX-3298", chen, "NORTHFIELD RESOURCES", TransactionSide.Buy, 15000, 342000, "2026-07-24", TransactionCategory.RelatedParty, TransactionStatus.Flagged,
                "Purchase in a subsidiary where R. Chen also holds a board seat. Requires disclosure under related-party transaction policy and confirmation the position was not initiated using material non-public information.",
                "RPT-2 — Related-Party Disclosure", "Pending assignment", "2026-07-24"),
            New("TX-3290", okafor, "VERISUN", TransactionSide.Buy, 800, 21600, "2026-07-23", TransactionCategory.PreClearance, TransactionStatus.Cleared,
                "Standard pre-clearance request approved same day; ticker not on restricted list, employee outside blackout window.",
                "ITP §3.1 — Pre-Clearance", "A. Bianchi, Compliance", "2026-07-23"),
            New("TX-3287", whitfield, "HALCYON MATERIALS", TransactionSide.Sell, 6000, 289500, "2026-07-22", TransactionCategory.ThresholdBreach, TransactionStatus.Pending,
                "Transaction exceeds the $250,000 single-trade reporting threshold for executive officers. Under review for Form 4 filing timeliness.",
                "TCP-1 — Reporting Threshold", "L. Park, Compliance", "2026-07-22"),
            New("TX-3281", dubois, "ATLASCO", TransactionSide.Buy, 1200, 33840, "2026-07-21", TransactionCategory.RestrictedList, TransactionStatus.Flagged,
                "ATLASCO added to the restricted list two days prior due to pending M&A discussions; trade executed after the list update. Reviewing whether employee had access to the restricted list notice.",
                "ITP §5.0 — Restricted List", "K. Osei, Compliance", "2026-07-21"),
            New("TX-3277", alvarez, "GREENPORT LOGISTICS", TransactionSide.Gift, 500, 14200, "2026-07-20", TransactionCategory.RelatedParty, TransactionStatus.Cleared,
                "Charitable gift of shares to a donor-advised fund; reviewed for related-party conflict, none identified.",
                "RPT-2 — Related-Party Disclosure", "A. Bianchi, Compliance", "2026-07-20"),
            New("TX-3270", nakamura, "VERISUN", TransactionSide.Sell, 9000, 243000, "2026-07-18", TransactionCategory.PreClearance, TransactionStatus.Cleared,
                "Pre-cleared sale under a 10b5-1 trading plan established in Q1; execution consistent with plan schedule.",
                "ITP §3.3 — 10b5-1 Plans", "L. Park, Compliance", "2026-07-18"),
            New("TX-3266", kowalski, "HALCYON MATERIALS", TransactionSide.Buy, 2100, 101430, "2026-07-17", TransactionCategory.ThresholdBreach, TransactionStatus.Cleared,
                "Exceeds standard threshold but within pre-approved annual allocation for Treasury officers; documentation on file.",
                "TCP-1 — Reporting Threshold", "A. Bianchi, Compliance", "2026-07-17"),
            New("TX-3259", okafor, "NORTHFIELD RESOURCES", TransactionSide.Sell, 300, 6870, "2026-07-16", TransactionCategory.PreClearance, TransactionStatus.Pending,
                "Pre-clearance request submitted after execution rather than before. Awaiting explanation from employee.",
                "ITP §3.1 — Pre-Clearance", "K. Osei, Compliance", "2026-07-16"),
            New("TX-3251", chen, "GREENPORT LOGISTICS", TransactionSide.Buy, 4000, 96400, "2026-07-15", TransactionCategory.RelatedParty, TransactionStatus.Pending,
                "Board member increasing position in a company with a shared supplier contract; assessing conflict-of-interest disclosure requirements.",
                "RPT-2 — Related-Party Disclosure", "L. Park, Compliance", "2026-07-15"),
            New("TX-3244", marlowe, "ATLASCO", TransactionSide.Sell, 1000, 27800, "2026-07-14", TransactionCategory.BlackoutPeriod, TransactionStatus.Cleared,
                "Sale pre-cleared under hardship exception granted prior to blackout window commencement.",
                "ITP §4.4 — Hardship Exception", "Compliance Committee", "2026-07-14"),
            New("TX-3238", whitfield, "VERISUN", TransactionSide.Buy, 2500, 67500, "2026-07-12", TransactionCategory.PreClearance, TransactionStatus.Cleared,
                "Routine pre-cleared purchase, no restrictions applicable.",
                "ITP §3.1 — Pre-Clearance", "A. Bianchi, Compliance", "2026-07-12"),
            New("TX-3229", dubois, "HALCYON MATERIALS", TransactionSide.Sell, 7500, 362250, "2026-07-11", TransactionCategory.ThresholdBreach, TransactionStatus.Flagged,
                "Large sale shortly after attending a materially sensitive strategy session; escalated for trading-window review pending confirmation session content was not price-sensitive.",
                "TCP-1 — Reporting Threshold", "K. Osei, Compliance", "2026-07-11"),
            New("TX-3220", kowalski, "GREENPORT LOGISTICS", TransactionSide.Buy, 600, 14460, "2026-07-10", TransactionCategory.PreClearance, TransactionStatus.Cleared,
                "Routine pre-cleared purchase, no restrictions applicable.",
                "ITP §3.1 — Pre-Clearance", "L. Park, Compliance", "2026-07-10"),
            New("TX-3212", nakamura, "NORTHFIELD RESOURCES", TransactionSide.Gift, 2000, 45800, "2026-07-09", TransactionCategory.RelatedParty, TransactionStatus.Cleared,
                "Gift transfer to family trust reviewed under related-party policy; no conflict identified as trust is independently administered.",
                "RPT-2 — Related-Party Disclosure", "A. Bianchi, Compliance", "2026-07-09"),
            New("TX-3203", alvarez, "VERISUN", TransactionSide.Buy, 3300, 89100, "2026-07-08", TransactionCategory.BlackoutPeriod, TransactionStatus.Cleared,
                "Executed outside applicable blackout window; pre-clearance approved.",
                "ITP §4.2 — Blackout Period", "K. Osei, Compliance", "2026-07-08"),
            New("TX-3195", okafor, "ATLASCO", TransactionSide.Sell, 1500, 42150, "2026-07-06", TransactionCategory.RestrictedList, TransactionStatus.Cleared,
                "Sale reviewed against restricted list effective date; ticker was cleared for trading at time of execution.",
                "ITP §5.0 — Restricted List", "L. Park, Compliance", "2026-07-06"),
            New("TX-3186", marlowe, "HALCYON MATERIALS", TransactionSide.Buy, 900, 43470, "2026-07-04", TransactionCategory.PreClearance, TransactionStatus.Cleared,
                "Routine pre-cleared purchase, no restrictions applicable.",
                "ITP §3.1 — Pre-Clearance", "A. Bianchi, Compliance", "2026-07-04"),
            New("TX-3177", chen, "HALCYON MATERIALS", TransactionSide.Sell, 5200, 251160, "2026-07-02", TransactionCategory.ThresholdBreach, TransactionStatus.Pending,
                "Exceeds single-trade reporting threshold; Form 4 amendment under preparation.",
                "TCP-1 — Reporting Threshold", "K. Osei, Compliance", "2026-07-02"),
            New("TX-3168", whitfield, "NORTHFIELD RESOURCES", TransactionSide.Buy, 1100, 25190, "2026-06-29", TransactionCategory.PreClearance, TransactionStatus.Cleared,
                "Routine pre-cleared purchase under existing 10b5-1 plan.",
                "ITP §3.3 — 10b5-1 Plans", "L. Park, Compliance", "2026-06-29"),
            New("TX-3159", dubois, "GREENPORT LOGISTICS", TransactionSide.Buy, 3000, 72300, "2026-06-25", TransactionCategory.RelatedParty, TransactionStatus.Cleared,
                "Position increase reviewed; partner relationship disclosed and deemed immaterial to independence.",
                "RPT-2 — Related-Party Disclosure", "A. Bianchi, Compliance", "2026-06-25"),
            New("TX-3150", kowalski, "VERISUN", TransactionSide.Sell, 1800, 48600, "2026-06-20", TransactionCategory.PreClearance, TransactionStatus.Cleared,
                "Routine pre-cleared sale, no restrictions applicable.",
                "ITP §3.1 — Pre-Clearance", "K. Osei, Compliance", "2026-06-20"),
        };

        db.Transactions.AddRange(t);

        db.ScheduledActivities.AddRange(
            new ScheduledActivity
            {
                Type = ScheduledActivityType.BlackoutPeriod,
                Dates =
                [
                    new ScheduledActivityDate { Month = 3, Day = 15 },
                    new ScheduledActivityDate { Month = 6, Day = 15 },
                    new ScheduledActivityDate { Month = 9, Day = 15 },
                    new ScheduledActivityDate { Month = 12, Day = 15 },
                ],
            },
            new ScheduledActivity
            {
                Type = ScheduledActivityType.InsiderTrading,
                Dates =
                [
                    new ScheduledActivityDate { Month = 1, Day = 15 },
                    new ScheduledActivityDate { Month = 10, Day = 15 },
                ],
            },
            new ScheduledActivity
            {
                Type = ScheduledActivityType.ConflictOfInterest,
                Dates = [new ScheduledActivityDate { Month = 1, Day = 1 }],
            });

        db.DeclarationSetups.AddRange(
            new DeclarationSetup { Type = DeclarationType.InsiderTrading, Date = new DateOnly(2026, 1, 15), DueDate = new DateOnly(2026, 1, 31) },
            new DeclarationSetup { Type = DeclarationType.ConflictOfInterest, Date = new DateOnly(2026, 1, 1), DueDate = new DateOnly(2026, 1, 31) });

        await db.SaveChangesAsync();
    }

    private static Transaction New(
        string referenceCode, Member member, string instrument, TransactionSide side, int quantity, decimal amount,
        string tradeDate, TransactionCategory category, TransactionStatus status, string note, string policy, string reviewer, string filedDate)
        => new()
        {
            ReferenceCode = referenceCode,
            Member = member,
            Instrument = instrument,
            Side = side,
            Quantity = quantity,
            Amount = amount,
            TradeDate = DateOnly.Parse(tradeDate),
            Category = category,
            Status = status,
            ComplianceNote = note,
            PolicyReference = policy,
            Reviewer = reviewer,
            FiledDate = DateOnly.Parse(filedDate)
        };
}

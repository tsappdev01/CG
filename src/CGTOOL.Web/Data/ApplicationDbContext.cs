using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using CGTOOL.Web.Data.Governance;

namespace CGTOOL.Web.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<Member> Members => Set<Member>();
    public DbSet<MemberImpersonationApproval> MemberImpersonationApprovals => Set<MemberImpersonationApproval>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();
    public DbSet<DeclarationSetup> DeclarationSetups => Set<DeclarationSetup>();
    public DbSet<ScheduledActivity> ScheduledActivities => Set<ScheduledActivity>();
    public DbSet<ScheduledActivityDate> ScheduledActivityDates => Set<ScheduledActivityDate>();
    public DbSet<DeclarationSubmission> DeclarationSubmissions => Set<DeclarationSubmission>();
    public DbSet<MemberNotification> MemberNotifications => Set<MemberNotification>();
    public DbSet<JobTitle> JobTitles => Set<JobTitle>();
    public DbSet<DeclarationCycleSetup> DeclarationCycleSetups => Set<DeclarationCycleSetup>();
    public DbSet<DeclarationCycleRun> DeclarationCycleRuns => Set<DeclarationCycleRun>();
    public DbSet<InsiderDeclaration> InsiderDeclarations => Set<InsiderDeclaration>();
    public DbSet<InsiderDeclarationRelative> InsiderDeclarationRelatives => Set<InsiderDeclarationRelative>();
    public DbSet<InsiderDeclarationNinHolder> InsiderDeclarationNinHolders => Set<InsiderDeclarationNinHolder>();
    public DbSet<DeclarationCycleRunRecipient> DeclarationCycleRunRecipients => Set<DeclarationCycleRunRecipient>();
    public DbSet<RelatedPartyCoiDeclaration> RelatedPartyCoiDeclarations => Set<RelatedPartyCoiDeclaration>();
    public DbSet<CoiRelative> CoiRelatives => Set<CoiRelative>();
    public DbSet<CoiCompanyEntry> CoiCompanyEntries => Set<CoiCompanyEntry>();
    public DbSet<CoiTradeLicenseDocument> CoiTradeLicenseDocuments => Set<CoiTradeLicenseDocument>();
    public DbSet<CoiConflictEntry> CoiConflictEntries => Set<CoiConflictEntry>();
    public DbSet<PolicyDocumentVersion> PolicyDocumentVersions => Set<PolicyDocumentVersion>();
    public DbSet<NavMenuItemOrder> NavMenuItemOrders => Set<NavMenuItemOrder>();
    public DbSet<NavMenuItemLabel> NavMenuItemLabels => Set<NavMenuItemLabel>();
    public DbSet<ShareholderRegisterUpload> ShareholderRegisterUploads => Set<ShareholderRegisterUpload>();
    public DbSet<ShareholderRecord> ShareholderRecords => Set<ShareholderRecord>();
    public DbSet<ShareTradingUpload> ShareTradingUploads => Set<ShareTradingUpload>();
    public DbSet<ShareTradingRecord> ShareTradingRecords => Set<ShareTradingRecord>();
    public DbSet<RelatedPartyTransaction> RelatedPartyTransactions => Set<RelatedPartyTransaction>();
    public DbSet<RelatedPartyTransactionDocument> RelatedPartyTransactionDocuments => Set<RelatedPartyTransactionDocument>();
    public DbSet<DeclarationReminderLog> DeclarationReminderLogs => Set<DeclarationReminderLog>();
    public DbSet<FamilyMember> FamilyMembers => Set<FamilyMember>();
    public DbSet<OwnedCompany> OwnedCompanies => Set<OwnedCompany>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Company>()
            .HasIndex(c => c.ShortCode)
            .IsUnique();

        builder.Entity<NavMenuItemOrder>()
            .HasIndex(n => n.ItemKey)
            .IsUnique();

        builder.Entity<NavMenuItemLabel>()
            .HasIndex(n => n.ItemKey)
            .IsUnique();

        builder.Entity<Company>()
            .HasOne(c => c.ApprovingAuthorityMember)
            .WithMany()
            .HasForeignKey(c => c.ApprovingAuthorityMemberId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Company>()
            .HasOne(c => c.DelegateAuthorityMember)
            .WithMany()
            .HasForeignKey(c => c.DelegateAuthorityMemberId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Department>()
            .HasIndex(d => d.Code)
            .IsUnique();

        builder.Entity<Member>()
            .HasOne(m => m.Company)
            .WithMany(c => c.Members)
            .HasForeignKey(m => m.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Member>()
            .HasOne(m => m.Department)
            .WithMany(d => d.Members)
            .HasForeignKey(m => m.DepartmentId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Entity<Member>()
            .HasOne(m => m.ReportingManager)
            .WithMany()
            .HasForeignKey(m => m.ReportingManagerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Member>()
            .HasOne(m => m.ApplicationUser)
            .WithMany()
            .HasForeignKey(m => m.ApplicationUserId)
            .OnDelete(DeleteBehavior.SetNull);

        // One login account can only ever back one Member -- a plain unique index on a nullable
        // SQL Server column would reject a second NULL, so this is filtered to only enforce
        // uniqueness once a login is actually linked.
        builder.Entity<Member>()
            .HasIndex(m => m.ApplicationUserId)
            .IsUnique()
            .HasFilter("[ApplicationUserId] IS NOT NULL");

        builder.Entity<MemberImpersonationApproval>()
            .HasOne(a => a.Member)
            .WithMany(m => m.ApprovedImpersonators)
            .HasForeignKey(a => a.MemberId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<MemberImpersonationApproval>()
            .HasOne(a => a.Impersonator)
            .WithMany()
            .HasForeignKey(a => a.ImpersonatorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<MemberImpersonationApproval>()
            .HasIndex(a => new { a.MemberId, a.ImpersonatorId })
            .IsUnique();

        builder.Entity<Transaction>()
            .HasIndex(t => t.ReferenceCode)
            .IsUnique();

        builder.Entity<Transaction>()
            .HasOne(t => t.Member)
            .WithMany(m => m.Transactions)
            .HasForeignKey(t => t.MemberId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<AuditLogEntry>()
            .HasIndex(a => a.OccurredAtUtc);

        builder.Entity<ScheduledActivityDate>()
            .HasOne(d => d.ScheduledActivity)
            .WithMany(a => a.Dates)
            .HasForeignKey(d => d.ScheduledActivityId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<ScheduledActivityDate>()
            .HasIndex(d => new { d.ScheduledActivityId, d.Month, d.Day })
            .IsUnique();

        builder.Entity<DeclarationSubmission>()
            .HasOne(s => s.Member)
            .WithMany()
            .HasForeignKey(s => s.MemberId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<DeclarationSubmission>()
            .HasOne(s => s.DeclarationSetup)
            .WithMany()
            .HasForeignKey(s => s.DeclarationSetupId)
            .OnDelete(DeleteBehavior.Restrict);

        // A member can only submit once per declaration period.
        builder.Entity<DeclarationSubmission>()
            .HasIndex(s => new { s.MemberId, s.DeclarationSetupId })
            .IsUnique();

        builder.Entity<MemberNotification>()
            .HasOne(n => n.Member)
            .WithMany()
            .HasForeignKey(n => n.MemberId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<MemberNotification>()
            .HasIndex(n => n.Status);

        builder.Entity<FamilyMember>()
            .HasOne(f => f.Member)
            .WithMany()
            .HasForeignKey(f => f.MemberId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<OwnedCompany>()
            .HasOne(o => o.Member)
            .WithMany()
            .HasForeignKey(o => o.MemberId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<OwnedCompany>()
            .Property(o => o.OwnershipPercentage)
            .HasPrecision(5, 2);

        builder.Entity<JobTitle>()
            .HasIndex(j => j.Name)
            .IsUnique();

        builder.Entity<DeclarationCycleSetup>()
            .HasIndex(s => s.Type)
            .IsUnique();

        builder.Entity<DeclarationCycleRun>()
            .HasOne(r => r.DeclarationCycleSetup)
            .WithMany()
            .HasForeignKey(r => r.DeclarationCycleSetupId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<DeclarationCycleRun>()
            .HasOne(r => r.Company)
            .WithMany()
            .HasForeignKey(r => r.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<InsiderDeclaration>()
            .HasOne(d => d.Member)
            .WithMany()
            .HasForeignKey(d => d.MemberId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<InsiderDeclaration>()
            .HasOne(d => d.DeclarationCycleRun)
            .WithMany()
            .HasForeignKey(d => d.DeclarationCycleRunId)
            .OnDelete(DeleteBehavior.Restrict);

        // One declaration per member per notification cycle.
        builder.Entity<InsiderDeclaration>()
            .HasIndex(d => new { d.MemberId, d.DeclarationCycleRunId })
            .IsUnique();

        builder.Entity<InsiderDeclarationRelative>()
            .HasOne(r => r.InsiderDeclaration)
            .WithMany(d => d.Relatives)
            .HasForeignKey(r => r.InsiderDeclarationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<InsiderDeclarationNinHolder>()
            .HasOne(r => r.InsiderDeclaration)
            .WithMany(d => d.NinHolders)
            .HasForeignKey(r => r.InsiderDeclarationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<DeclarationCycleRunRecipient>()
            .HasOne(r => r.DeclarationCycleRun)
            .WithMany(r => r.Recipients)
            .HasForeignKey(r => r.DeclarationCycleRunId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<ShareholderRecord>()
            .HasOne(r => r.ShareholderRegisterUpload)
            .WithMany(u => u.Records)
            .HasForeignKey(r => r.ShareholderRegisterUploadId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<ShareholderRecord>()
            .HasIndex(r => new { r.ShareholderRegisterUploadId, r.Nin });

        // Qty runs into the billions (DIC's total share count is ~4.25bn) but %Qty needs four decimal
        // places to keep tiny holdings (e.g. 0.0005%) from rounding to zero -- the default decimal(18,2)
        // EF Core falls back to would silently truncate those.
        builder.Entity<ShareholderRecord>().Property(r => r.Qty).HasPrecision(18, 2);
        builder.Entity<ShareholderRecord>().Property(r => r.QtyPercent).HasPrecision(9, 4);
        builder.Entity<ShareholderRecord>().Property(r => r.Frozen).HasPrecision(18, 2);

        builder.Entity<ShareholderRegisterUpload>()
            .HasIndex(u => u.AsOnDate);

        builder.Entity<ShareTradingRecord>()
            .HasOne(r => r.ShareTradingUpload)
            .WithMany(u => u.Records)
            .HasForeignKey(r => r.ShareTradingUploadId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<ShareTradingRecord>()
            .HasIndex(r => new { r.ReportDate, r.Nin });

        builder.Entity<ShareTradingRecord>().Property(r => r.PreviousOwnQty).HasPrecision(18, 2);
        builder.Entity<ShareTradingRecord>().Property(r => r.CurrentOwnQty).HasPrecision(18, 2);
        builder.Entity<ShareTradingRecord>().Property(r => r.OwnedQtyChange).HasPrecision(18, 2);

        builder.Entity<RelatedPartyTransaction>()
            .HasOne(t => t.Company)
            .WithMany()
            .HasForeignKey(t => t.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<RelatedPartyTransaction>()
            .HasOne(t => t.Member)
            .WithMany()
            .HasForeignKey(t => t.MemberId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<RelatedPartyTransaction>()
            .HasOne(t => t.ApproverMember)
            .WithMany()
            .HasForeignKey(t => t.ApproverMemberId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<RelatedPartyTransaction>()
            .HasIndex(t => t.Status);

        builder.Entity<RelatedPartyTransactionDocument>()
            .HasOne(d => d.RelatedPartyTransaction)
            .WithMany(t => t.Documents)
            .HasForeignKey(d => d.RelatedPartyTransactionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<DeclarationReminderLog>()
            .HasIndex(l => new { l.MemberId, l.Category, l.Year, l.Quarter });
    }
}

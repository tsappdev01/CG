using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using CGTOOL.Web.Components;
using CGTOOL.Web.Components.Account;
using CGTOOL.Web.Components.Layout;
using CGTOOL.Web.Data;
using CGTOOL.Web.Data.Governance;

var builder = WebApplication.CreateBuilder(args);

// PDFsharp has no GDI to resolve fonts from on .NET -- must be set once, globally, before any
// XFont is constructed (used by InsiderDeclarationPdfBuilder for the confirmation-email attachment).
PdfSharp.Fonts.GlobalFontSettings.FontResolver = new PdfFontResolver(builder.Environment);

// ExcelDataReader needs this registered before the first CreateReader call to read legacy code-page
// text some .xlsx exports still carry (Investor Relations uploads) -- otherwise it throws
// NotSupportedException on those cells instead of just reading them.
System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents(options => options.DetailedErrors = builder.Environment.IsDevelopment());

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityUserAccessor>();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

var authBuilder = builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = IdentityConstants.ApplicationScheme;
    options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
});
authBuilder.AddIdentityCookies();

// UM-02: Azure AD SSO, wired as an external login provider feeding into Identity's own cookie
// scheme (so existing role-based [Authorize] checks keep working unchanged). Only registered when
// a real Azure AD app registration is configured; otherwise the login page just shows the
// corporate-directory form (used by External Members, who have no Azure AD account, and in
// Development where no Azure tenant is reachable).
var azureAdSection = builder.Configuration.GetSection("AzureAd");
if (!string.IsNullOrWhiteSpace(azureAdSection["ClientId"]) && !string.IsNullOrWhiteSpace(azureAdSection["TenantId"]))
{
    authBuilder.AddOpenIdConnect("AzureAD", "Sign in with Microsoft", options =>
    {
        options.SignInScheme = IdentityConstants.ExternalScheme;
        var instance = (azureAdSection["Instance"] ?? "https://login.microsoftonline.com/").TrimEnd('/');
        options.Authority = $"{instance}/{azureAdSection["TenantId"]}/v2.0";
        options.ClientId = azureAdSection["ClientId"];
        options.ClientSecret = azureAdSection["ClientSecret"];
        options.ResponseType = Microsoft.IdentityModel.Protocols.OpenIdConnect.OpenIdConnectResponseType.Code;
        options.SaveTokens = true;
        options.CallbackPath = "/signin-oidc-azuread";
        options.Scope.Add("User.Read");
    });
}

builder.Services.AddHttpClient();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
builder.Services.AddDbContextFactory<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));
builder.Services.AddScoped<ApplicationDbContext>(sp =>
    sp.GetRequiredService<IDbContextFactory<ApplicationDbContext>>().CreateDbContext());
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

// No outbound email sender is wired up for Identity's own confirmation-link flow (IdentityNoOpEmailSender
// is a genuine no-op, not a real transport) -- requiring confirmation would otherwise strand every new
// self-registered account on a "click here to confirm" page instead of signing them in.
builder.Services.AddIdentityCore<ApplicationUser>(options => options.SignIn.RequireConfirmedAccount = false)
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

builder.Services.AddAuthorization();

builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();
builder.Services.AddScoped<IAuditLogger, AuditLogger>();

// All writes (insert/update/deactivate/delete) on the app's own domain tables go through stored
// procedures (see scripts/stored-procedures.sql), executed against the same connection/transaction
// as the current DbContext. Reads stay as LINQ/EF Core. ASP.NET Core Identity's own tables/APIs
// (UserManager/RoleManager/SignInManager) are intentionally out of scope.
builder.Services.AddScoped<IStoredProcedureExecutor, StoredProcedureExecutor>();
builder.Services.AddScoped<ICompanyWriter, CompanyWriter>();
builder.Services.AddScoped<IDepartmentWriter, DepartmentWriter>();
builder.Services.AddScoped<IMemberWriter, MemberWriter>();
builder.Services.AddScoped<IMemberImpersonationApprovalWriter, MemberImpersonationApprovalWriter>();
builder.Services.AddScoped<ITransactionWriter, TransactionWriter>();
builder.Services.AddScoped<IDeclarationSetupWriter, DeclarationSetupWriter>();
builder.Services.AddScoped<IDeclarationSubmissionWriter, DeclarationSubmissionWriter>();
builder.Services.AddScoped<IScheduledActivityWriter, ScheduledActivityWriter>();
builder.Services.AddScoped<IMemberNotificationWriter, MemberNotificationWriter>();
builder.Services.AddScoped<IJobTitleWriter, JobTitleWriter>();
builder.Services.AddScoped<IDeclarationCycleSetupWriter, DeclarationCycleSetupWriter>();
builder.Services.AddScoped<IDeclarationCycleRunWriter, DeclarationCycleRunWriter>();
builder.Services.AddScoped<IInsiderDeclarationWriter, InsiderDeclarationWriter>();
builder.Services.AddScoped<IRelatedPartyCoiDeclarationWriter, RelatedPartyCoiDeclarationWriter>();
builder.Services.AddScoped<IPolicyDocumentVersionWriter, PolicyDocumentVersionWriter>();
builder.Services.AddScoped<INavMenuItemOrderWriter, NavMenuItemOrderWriter>();
builder.Services.AddScoped<INavMenuItemLabelWriter, NavMenuItemLabelWriter>();
builder.Services.AddScoped<INavMenuItemVisibilityWriter, NavMenuItemVisibilityWriter>();
builder.Services.AddScoped<IDirectoryImporter, DirectoryImporter>();
builder.Services.AddScoped<IAuditLogIntegrity, AuditLogIntegrity>();
builder.Services.AddScoped<IAuditLogReviewWriter, AuditLogReviewWriter>();
builder.Services.AddScoped<NavMenuStateNotifier>();
builder.Services.AddScoped<IShareholderRegisterWriter, ShareholderRegisterWriter>();
builder.Services.AddScoped<IShareTradingWriter, ShareTradingWriter>();
builder.Services.AddScoped<IRelatedPartyTransactionWriter, RelatedPartyTransactionWriter>();
builder.Services.AddScoped<IDeclarationReminderLogWriter, DeclarationReminderLogWriter>();
builder.Services.AddScoped<IFamilyMemberWriter, FamilyMemberWriter>();
builder.Services.AddScoped<IOwnedCompanyWriter, OwnedCompanyWriter>();

builder.Services.AddScoped<ImpersonationContext>();
builder.Services.AddScoped<ToastService>();
builder.Services.AddScoped<ClientContext>();
builder.Services.AddHttpContextAccessor();

// SQL Server Database Mail (Smtp:SqlDbMailProfile, e.g. "CGS") is the only outbound mail path --
// SMTP (internal relay / Gmail) is no longer used. Falls back to logging only when not configured,
// so the schedule/welcome-email logic is still observable without a real mailbox.
var sqlDbMailConfigured = !string.IsNullOrWhiteSpace(builder.Configuration["Smtp:SqlDbMailProfile"]);

// Registered unconditionally (not just when it's the preferred IActivityEmailSender) so pages that
// specifically need SQL DB Mail's query-result-as-attachment feature (e.g. the Insider Trading
// declaration confirmation) can inject it directly; SqlDbMailSender.IsConfigured guards actual use.
// Scoped, not singleton: it sends through the current DbContext's own SQL connection (via
// IStoredProcedureExecutor), which is itself scoped per circuit/request.
builder.Services.AddScoped<SqlDbMailSender>();

if (sqlDbMailConfigured)
{
    builder.Services.AddScoped<IActivityEmailSender, SqlDbMailActivityEmailSender>();
    builder.Services.AddScoped<IMemberWelcomeEmailSender, SqlDbMailMemberWelcomeEmailSender>();
}
else
{
    builder.Services.AddSingleton<IActivityEmailSender, LoggingActivityEmailSender>();
    builder.Services.AddSingleton<IMemberWelcomeEmailSender, NoOpMemberWelcomeEmailSender>();
}

builder.Services.AddHostedService<ScheduledActivityHostedService>();
builder.Services.AddHostedService<DeclarationReminderHostedService>();
builder.Services.AddHostedService<DeclarationCycleReminderHostedService>();
builder.Services.AddHostedService<RelatedPartyTransactionReminderHostedService>();

// Login validates against the corporate directory (matches the "corporate/windows credentials"
// login mockup) when one is actually configured; otherwise local login falls back to checking the
// local Identity password hash, so the app works standalone until real LDAP/Azure AD is wired up,
// regardless of ASPNETCORE_ENVIRONMENT.
var adConfigured = !string.IsNullOrWhiteSpace(builder.Configuration["ActiveDirectory:Server"]);
if (adConfigured)
{
    builder.Services.AddScoped<IActiveDirectoryAuthenticator, LdapActiveDirectoryAuthenticator>();
}
else
{
    builder.Services.AddScoped<IActiveDirectoryAuthenticator, LocalIdentityAuthenticator>();
}

var graphConfigured = !string.IsNullOrWhiteSpace(azureAdSection["ClientSecret"]) && !string.IsNullOrWhiteSpace(azureAdSection["TenantId"]);
if (graphConfigured)
{
    builder.Services.AddSingleton<IDirectoryEmployeeProvider, GraphDirectoryEmployeeProvider>();
    // Loads the whole tenant into the member directory -- see EntraDirectorySync. Scoped, not
    // singleton: it writes through the stored-procedure writers on the current DbContext.
    builder.Services.AddScoped<IEntraDirectorySync, EntraDirectorySync>();
}
else
{
    builder.Services.AddSingleton<IDirectoryEmployeeProvider, NotConfiguredDirectoryEmployeeProvider>();
    builder.Services.AddScoped<IEntraDirectorySync, NotConfiguredEntraDirectorySync>();
}

// Insider Trading declaration wizard's ID document auto-capture (Functional Spec §3.3). Configure
// via user-secrets in dev or Key Vault / App Service Application Settings in production --
// DocumentIntelligence:Endpoint / DocumentIntelligence:ApiKey must never be committed to
// appsettings.json or any other file in source control.
var documentIntelligenceConfigured = !string.IsNullOrWhiteSpace(builder.Configuration["DocumentIntelligence:Endpoint"])
    && !string.IsNullOrWhiteSpace(builder.Configuration["DocumentIntelligence:ApiKey"]);
if (documentIntelligenceConfigured)
{
    builder.Services.AddSingleton<IDocumentIntelligenceService, DocumentIntelligenceService>();
}
else
{
    builder.Services.AddSingleton<IDocumentIntelligenceService, NotConfiguredDocumentIntelligenceService>();
}

// Related Party & COI declaration submission gate (UAE PASS identity verification -- see
// UaePassAuthService). Configure UaePass:ClientId/ClientSecret via user-secrets in dev or Key Vault /
// App Service Application Settings in production -- never commit real values to appsettings.json.
var uaePassConfigured = !string.IsNullOrWhiteSpace(builder.Configuration["UaePass:ClientId"])
    && !string.IsNullOrWhiteSpace(builder.Configuration["UaePass:ClientSecret"]);
builder.Services.AddHttpClient("UaePass", http =>
{
    var baseUrl = builder.Configuration["UaePass:BaseUrl"];
    if (!string.IsNullOrWhiteSpace(baseUrl)) http.BaseAddress = new Uri(baseUrl);
});
if (uaePassConfigured)
{
    builder.Services.AddScoped<IUaePassAuthService, UaePassAuthService>();
}
else
{
    builder.Services.AddScoped<IUaePassAuthService, NotConfiguredUaePassAuthService>();
}
// Protects the declarationId carried in the UAE PASS "state" round-trip so the /uaepass/callback
// endpoint can trust it without needing server-side session storage across the external redirect.
builder.Services.AddSingleton(sp => sp.GetRequiredService<IDataProtectionProvider>().CreateProtector("UaePassState"));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await db.Database.MigrateAsync();

    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    foreach (var role in GovernanceRoles.All)
    {
        if (!await roleManager.RoleExistsAsync(role))
        {
            await roleManager.CreateAsync(new IdentityRole(role));
        }
    }

    // A brand new deployment has no accounts at all, so nobody could sign in to create the first
    // one. This provisions a bootstrap administrator, and retires it again as soon as a real
    // administrator exists -- see DefaultAdminProvisioner for the full rule.
    var startupLogger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
    await DefaultAdminProvisioner.EnsureAsync(
        scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(),
        builder.Configuration,
        startupLogger);

    // Demo fixture data (sample entities, departments, members and transactions). Off unless a
    // deployment asks for it, so a real database is never populated with invented people.
    if (builder.Configuration.GetValue("Seed:DemoData", false))
    {
        startupLogger.LogWarning("Seed:DemoData is enabled -- seeding demonstration entities, departments, members and transactions.");
        await GovernanceSeeder.SeedAsync(db);
    }
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Add additional endpoints required by the Identity /Account Razor components.
app.MapAdditionalIdentityEndpoints();
app.MapAdminImpersonationEndpoints();

// UAE PASS OAuth2 login callback (see UaePassAuthService/SubmitRelatedPartyCoiDeclaration). A plain
// minimal API endpoint, not a Blazor component -- the external redirect out to UAE PASS and back
// tears down the originating Blazor circuit, so there's no component instance left to resume; "state"
// (an IDataProtector-protected declarationId) is how the callback recovers which declaration this
// login was for without needing server-side session storage.
// The template for the user-list upload, generated from the parser's own list of headings so the
// two cannot drift apart. A sample row is included because the shape of Role and Reporting Manager
// is easier to show than to describe.
app.MapGet("/admin/user-list-template.csv", () =>
{
    var headers = string.Join(",", UserListWorkbookParser.TemplateHeaders);
    var sample = string.Join("\n", new[]
    {
        "Jane Smith,jsmith,Finance,Manager - Operations,jane.smith@example.com,Example Entity,Normal User,",
        "Sam Patel,spatel,Finance,Financial Controller,sam.patel@example.com,Example Entity,Administrator,jane.smith@example.com",
    });

    return Results.Text($"{headers}\n{sample}\n", "text/csv", System.Text.Encoding.UTF8);
}).RequireAuthorization(policy => policy.RequireRole(GovernanceRoles.Administrator));

app.MapGet("/uaepass/callback", async (
    string? code,
    string? state,
    string? error,
    HttpRequest request,
    IUaePassAuthService uaePass,
    IDataProtector stateProtector,
    ApplicationDbContext db,
    IRelatedPartyCoiDeclarationWriter writer,
    IAuditLogger auditLog,
    ClientContext clientContext) =>
{
    int? declarationId = null;
    if (!string.IsNullOrEmpty(state))
    {
        try { declarationId = int.Parse(stateProtector.Unprotect(state)); }
        catch { declarationId = null; }
    }

    string ResultUrl(string flag) => declarationId is { } id
        ? $"/declarations/related-party-coi/{id}?uaepass={flag}"
        : $"/my-declarations?uaepass={flag}";

    if (!string.IsNullOrEmpty(error) || string.IsNullOrEmpty(code) || declarationId is null)
    {
        return Results.Redirect(ResultUrl("error"));
    }

    var redirectUri = uaePass.ResolveRedirectUri($"{request.Scheme}://{request.Host}/uaepass/callback");
    var identity = await uaePass.HandleCallbackAsync(code, redirectUri);
    if (identity is null)
    {
        return Results.Redirect(ResultUrl("error"));
    }

    var declaration = await db.RelatedPartyCoiDeclarations.FindAsync(declarationId.Value);
    if (declaration is null)
    {
        return Results.Redirect("/my-declarations?uaepass=error");
    }

    await writer.SetUaePassVerificationAsync(declaration.Id, DateTime.UtcNow, identity.FullName, identity.Uuid);

    clientContext.CaptureOnce(request.HttpContext);
    await auditLog.LogAsync(identity.FullName, AuditAction.Update, nameof(RelatedPartyCoiDeclaration), declaration.Id.ToString(),
        $"UAE PASS identity verification completed (uuid {identity.Uuid}).");

    return Results.Redirect(ResultUrl("verified"));
});

app.Run();

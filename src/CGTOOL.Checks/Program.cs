using CGTOOL.Checks;

Console.WriteLine("CGTOOL consistency checks");

RelationshipChecks.Run();
ReportAccessChecks.Run();
IdentityFormatChecks.Run();
ReportRenderingChecks.Run();
CountryChecks.Run();
OnboardingTemplateChecks.Run();
ExpiryChecks.Run();
AuditTrailChecks.Run();

return Check.Report();

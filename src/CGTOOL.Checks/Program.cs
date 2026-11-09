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
DirectoryFillChecks.Run();
DeclarationDocumentChecks.Run();
UploadLimitChecks.Run();

return Check.Report();

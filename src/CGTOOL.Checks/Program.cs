using CGTOOL.Checks;

Console.WriteLine("CGTOOL consistency checks");

RelationshipChecks.Run();
ReportAccessChecks.Run();
ReportAccessChecks.DetailSheetsAreAdministrators();
IdentityFormatChecks.Run();
ReportRenderingChecks.Run();
ReportRenderingChecks.DetailTables();
CountryChecks.Run();
OnboardingTemplateChecks.Run();
ExpiryChecks.Run();
AuditTrailChecks.Run();
DirectoryFillChecks.Run();
DeclarationDocumentChecks.Run();
UploadLimitChecks.Run();
UploadControlChecks.Run();
SubmissionFlowChecks.Run();

return Check.Report();

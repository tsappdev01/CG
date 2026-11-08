using CGTOOL.Checks;

Console.WriteLine("CGTOOL consistency checks");

RelationshipChecks.Run();
ReportAccessChecks.Run();
IdentityFormatChecks.Run();
ReportRenderingChecks.Run();

return Check.Report();

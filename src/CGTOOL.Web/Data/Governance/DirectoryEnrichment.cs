namespace CGTOOL.Web.Data.Governance;

/// <summary>Filling in what a spreadsheet does not say, from the company directory.
///
/// The onboarding template is a list of people, not a personnel record: it carries whatever the
/// person preparing it typed, which in practice is a first name and an address. Everyone imported
/// from one therefore arrived with no job title and no department, and the only way to get them
/// was to open each record afterwards and press "Fill from directory" one at a time.
///
/// So the upload asks the directory about each row before importing it. The match is made on
/// identity alone -- the Entra object id, or the address -- never on the name: a bare first name
/// matching one of several people who share it is exactly how one person's job title ends up on
/// another's record.
/// </summary>
public static class DirectoryEnrichment
{
    /// <summary>The row as the directory describes the same person.
    ///
    /// The directory wins for the three fields it is the record of -- name, job title, department
    /// -- because the match was exact and the spreadsheet's version of them is a transcription.
    /// Everything the directory does not carry (the entity the file files them under, their
    /// Emirates ID details, whether the account is enabled) is left exactly as the file had it.</summary>
    public static DirectoryPerson Merge(DirectoryPerson row, DirectoryEmployeeRecord record)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(record);

        return row with
        {
            // The id the file could never supply. It makes every later sync match this person
            // exactly, and it is what the profile photo is fetched by.
            ObjectId = Pick(record.SystemId, row.ObjectId),
            DisplayName = Pick(record.FullName, row.DisplayName) ?? row.DisplayName,
            JobTitle = Pick(record.JobTitle, row.JobTitle),
            Department = Pick(record.DepartmentName, row.Department),
        };
    }

    /// <summary>True when the directory had something to add to this row.</summary>
    public static bool Changed(DirectoryPerson before, DirectoryPerson after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        return before.ObjectId != after.ObjectId
            || before.DisplayName != after.DisplayName
            || before.JobTitle != after.JobTitle
            || before.Department != after.Department;
    }

    private static string? Pick(string? fromDirectory, string? fromFile) =>
        string.IsNullOrWhiteSpace(fromDirectory) ? fromFile : fromDirectory.Trim();
}

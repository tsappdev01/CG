using System.ComponentModel.DataAnnotations;

namespace CGTOOL.Web.Data.Governance;

/// <summary>One historical copy of the corporate policy document (Settings &gt; Upload Policy
/// Document). Every replacement backs up the file that was live at wwwroot/assets/policy.pdf under
/// FilePath before overwriting it, so nothing is ever lost -- this is the complete change log of who
/// replaced the policy document and when, with a link back to every prior version.</summary>
public class PolicyDocumentVersion
{
    public int Id { get; set; }

    /// <summary>Path (under wwwroot) to this version's backed-up copy.</summary>
    [Required, MaxLength(260)]
    public string FilePath { get; set; } = string.Empty;

    /// <summary>The file name as the uploader's browser reported it, before renaming to policy.pdf.</summary>
    [Required, MaxLength(260)]
    public string OriginalFileName { get; set; } = string.Empty;

    public DateTime UploadedAtUtc { get; set; } = DateTime.UtcNow;

    [Required, MaxLength(160)]
    public string UploadedByName { get; set; } = string.Empty;
}

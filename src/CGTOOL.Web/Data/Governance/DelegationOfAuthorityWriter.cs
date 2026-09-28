using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace CGTOOL.Web.Data.Governance;

public interface IDelegationOfAuthorityWriter
{
    /// <summary>Saves one entity's matrix whole -- the settings and every band, in one transaction.
    /// The bands replace whatever was there rather than merging: the editor hands over the complete
    /// set, and a merge would have to guess which row on screen used to be which row in the table.</summary>
    Task SaveAsync(DelegationOfAuthority matrix);

    /// <summary>Removes an entity's matrix, putting it back on the behaviour that existed before
    /// there was one: a single approver, whatever the value.</summary>
    Task DeleteAsync(int companyId);
}

public class DelegationOfAuthorityWriter(IStoredProcedureExecutor sp) : IDelegationOfAuthorityWriter
{
    public Task SaveAsync(DelegationOfAuthority matrix)
    {
        var bands = matrix.Bands
            .OrderBy(b => b.FromValue)
            .Select(b => new { b.FromValue, b.ToValue, Authority = (int)b.Authority });

        return sp.ExecuteAsync(
            "dbo.usp_DelegationOfAuthority_Save",
            new SqlParameter("@CompanyId", matrix.CompanyId),
            new SqlParameter("@EffectiveFrom", matrix.EffectiveFrom),
            new SqlParameter("@ApproverLimit", (object?)matrix.ApproverLimit ?? DBNull.Value),
            new SqlParameter("@EscalateOnApproverConflict", matrix.EscalateOnApproverConflict),
            new SqlParameter("@EscalateAfterDays", (object?)matrix.EscalateAfterDays ?? DBNull.Value),
            new SqlParameter("@BandsJson", JsonSerializer.Serialize(bands)));
    }

    public Task DeleteAsync(int companyId) => sp.ExecuteAsync(
        "dbo.usp_DelegationOfAuthority_Delete",
        new SqlParameter("@CompanyId", companyId));
}

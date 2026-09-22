using Microsoft.Data.SqlClient;

namespace CGTOOL.Web.Data.Governance;

public interface INavMenuItemLabelWriter
{
    Task UpsertAsync(string itemKey, string customLabel);
    Task ResetAsync(string itemKey);
}

public class NavMenuItemLabelWriter(IStoredProcedureExecutor sp) : INavMenuItemLabelWriter
{
    public Task UpsertAsync(string itemKey, string customLabel) => sp.ExecuteAsync(
        "dbo.usp_NavMenuItemLabel_Upsert",
        new SqlParameter("@ItemKey", itemKey),
        new SqlParameter("@CustomLabel", customLabel));

    public Task ResetAsync(string itemKey) => sp.ExecuteAsync(
        "dbo.usp_NavMenuItemLabel_Delete",
        new SqlParameter("@ItemKey", itemKey));
}

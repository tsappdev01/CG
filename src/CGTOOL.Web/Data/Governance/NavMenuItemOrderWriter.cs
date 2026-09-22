using Microsoft.Data.SqlClient;

namespace CGTOOL.Web.Data.Governance;

public interface INavMenuItemOrderWriter
{
    Task UpsertAsync(string itemKey, int sortOrder);
}

public class NavMenuItemOrderWriter(IStoredProcedureExecutor sp) : INavMenuItemOrderWriter
{
    public Task UpsertAsync(string itemKey, int sortOrder) => sp.ExecuteAsync(
        "dbo.usp_NavMenuItemOrder_Upsert",
        new SqlParameter("@ItemKey", itemKey),
        new SqlParameter("@SortOrder", sortOrder));
}

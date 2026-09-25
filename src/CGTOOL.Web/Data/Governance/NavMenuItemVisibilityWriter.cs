using Microsoft.Data.SqlClient;

namespace CGTOOL.Web.Data.Governance;

public interface INavMenuItemVisibilityWriter
{
    Task SetStateAsync(string itemKey, NavMenuItemState state);
}

public class NavMenuItemVisibilityWriter(IStoredProcedureExecutor sp) : INavMenuItemVisibilityWriter
{
    /// <summary>Visible is stored as the absence of a row, so setting it deletes the override.</summary>
    public Task SetStateAsync(string itemKey, NavMenuItemState state) =>
        state == NavMenuItemState.Visible
            ? sp.ExecuteAsync("dbo.usp_NavMenuItemVisibility_Delete", new SqlParameter("@ItemKey", itemKey))
            : sp.ExecuteAsync(
                "dbo.usp_NavMenuItemVisibility_Upsert",
                new SqlParameter("@ItemKey", itemKey),
                new SqlParameter("@State", (int)state));
}

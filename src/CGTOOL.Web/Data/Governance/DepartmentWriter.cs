using Microsoft.Data.SqlClient;

namespace CGTOOL.Web.Data.Governance;

public interface IDepartmentWriter
{
    Task<int> InsertAsync(Department department);
    Task UpdateAsync(Department department);
    Task SetActiveAsync(int id, bool active);
}

public class DepartmentWriter(IStoredProcedureExecutor sp) : IDepartmentWriter
{
    public Task<int> InsertAsync(Department department) => sp.InsertAsync("dbo.usp_Department_Insert",
        new SqlParameter("@Code", department.Code),
        new SqlParameter("@Name", department.Name),
        new SqlParameter("@Active", department.Active));

    public Task UpdateAsync(Department department) => sp.ExecuteAsync("dbo.usp_Department_Update",
        new SqlParameter("@Id", department.Id),
        new SqlParameter("@Code", department.Code),
        new SqlParameter("@Name", department.Name),
        new SqlParameter("@Active", department.Active));

    public Task SetActiveAsync(int id, bool active) => sp.ExecuteAsync("dbo.usp_Department_SetActive",
        new SqlParameter("@Id", id), new SqlParameter("@Active", active));
}

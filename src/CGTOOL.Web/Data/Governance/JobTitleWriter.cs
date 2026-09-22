using Microsoft.Data.SqlClient;

namespace CGTOOL.Web.Data.Governance;

public interface IJobTitleWriter
{
    Task<int> InsertAsync(JobTitle jobTitle);
    Task UpdateAsync(JobTitle jobTitle);
    Task SetActiveAsync(int id, bool active);
}

public class JobTitleWriter(IStoredProcedureExecutor sp) : IJobTitleWriter
{
    public Task<int> InsertAsync(JobTitle jobTitle) => sp.InsertAsync("dbo.usp_JobTitle_Insert",
        new SqlParameter("@Name", jobTitle.Name),
        new SqlParameter("@Active", jobTitle.Active));

    public Task UpdateAsync(JobTitle jobTitle) => sp.ExecuteAsync("dbo.usp_JobTitle_Update",
        new SqlParameter("@Id", jobTitle.Id),
        new SqlParameter("@Name", jobTitle.Name),
        new SqlParameter("@Active", jobTitle.Active));

    public Task SetActiveAsync(int id, bool active) => sp.ExecuteAsync("dbo.usp_JobTitle_SetActive",
        new SqlParameter("@Id", id), new SqlParameter("@Active", active));
}

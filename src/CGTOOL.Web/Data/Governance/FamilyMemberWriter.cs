using Microsoft.Data.SqlClient;

namespace CGTOOL.Web.Data.Governance;

public interface IFamilyMemberWriter
{
    Task<int> InsertAsync(FamilyMember familyMember);
    Task UpdateAsync(FamilyMember familyMember);
    Task DeleteAsync(int id);
}

public class FamilyMemberWriter(IStoredProcedureExecutor sp) : IFamilyMemberWriter
{
    public Task<int> InsertAsync(FamilyMember familyMember) => sp.InsertAsync("dbo.usp_FamilyMember_Insert",
        new SqlParameter("@MemberId", familyMember.MemberId),
        new SqlParameter("@Name", familyMember.Name),
        new SqlParameter("@Relationship", (int)familyMember.Relationship));

    public Task UpdateAsync(FamilyMember familyMember) => sp.ExecuteAsync("dbo.usp_FamilyMember_Update",
        new SqlParameter("@Id", familyMember.Id),
        new SqlParameter("@Name", familyMember.Name),
        new SqlParameter("@Relationship", (int)familyMember.Relationship),
        new SqlParameter("@EmiratesIdPath", (object?)familyMember.EmiratesIdPath ?? DBNull.Value),
        new SqlParameter("@PassportPath", (object?)familyMember.PassportPath ?? DBNull.Value));

    public Task DeleteAsync(int id) => sp.ExecuteAsync("dbo.usp_FamilyMember_Delete", new SqlParameter("@Id", id));
}

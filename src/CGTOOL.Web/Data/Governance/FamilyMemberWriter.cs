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
        new SqlParameter("@Relationship", (int)familyMember.Relationship),
        new SqlParameter("@IdentificationNumber", (object?)familyMember.IdentificationNumber ?? DBNull.Value),
        new SqlParameter("@DateOfBirth", familyMember.DateOfBirth is { } dob ? dob.ToDateTime(TimeOnly.MinValue) : DBNull.Value),
        new SqlParameter("@Nationality", (object?)familyMember.Nationality ?? DBNull.Value),
        new SqlParameter("@Occupation", (object?)familyMember.Occupation ?? DBNull.Value),
        new SqlParameter("@Organization", (object?)familyMember.Organization ?? DBNull.Value),
        new SqlParameter("@InterestType", (int)familyMember.InterestType),
        new SqlParameter("@OwnershipPercentage", (object?)familyMember.OwnershipPercentage ?? DBNull.Value));

    public Task UpdateAsync(FamilyMember familyMember) => sp.ExecuteAsync("dbo.usp_FamilyMember_Update",
        new SqlParameter("@Id", familyMember.Id),
        new SqlParameter("@Name", familyMember.Name),
        new SqlParameter("@Relationship", (int)familyMember.Relationship),
        new SqlParameter("@EmiratesIdPath", (object?)familyMember.EmiratesIdPath ?? DBNull.Value),
        new SqlParameter("@PassportPath", (object?)familyMember.PassportPath ?? DBNull.Value),
        new SqlParameter("@IdentificationNumber", (object?)familyMember.IdentificationNumber ?? DBNull.Value),
        new SqlParameter("@DateOfBirth", familyMember.DateOfBirth is { } dob ? dob.ToDateTime(TimeOnly.MinValue) : DBNull.Value),
        new SqlParameter("@Nationality", (object?)familyMember.Nationality ?? DBNull.Value),
        new SqlParameter("@Occupation", (object?)familyMember.Occupation ?? DBNull.Value),
        new SqlParameter("@Organization", (object?)familyMember.Organization ?? DBNull.Value),
        new SqlParameter("@InterestType", (int)familyMember.InterestType),
        new SqlParameter("@OwnershipPercentage", (object?)familyMember.OwnershipPercentage ?? DBNull.Value));

    public Task DeleteAsync(int id) => sp.ExecuteAsync("dbo.usp_FamilyMember_Delete", new SqlParameter("@Id", id));
}

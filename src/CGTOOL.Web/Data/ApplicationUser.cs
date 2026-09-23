using Microsoft.AspNetCore.Identity;

namespace CGTOOL.Web.Data;

// Add profile data for application users by adding properties to the ApplicationUser class
public class ApplicationUser : IdentityUser
{
    /// <summary>Web-relative path (under wwwroot) to the uploaded profile picture, e.g. "/uploads/profile-pictures/{id}.jpg". Null = no picture uploaded.</summary>
    public string? ProfilePicturePath { get; set; }

    /// <summary>True only for the bootstrap account created by <see cref="Governance.DefaultAdminProvisioner"/>
    /// so a brand new deployment can be signed into at all. It is a flag on the row rather than a
    /// well-known user name so that renaming the account (or changing DefaultAdmin:UserName later)
    /// cannot orphan it, and so nothing else can impersonate the bootstrap identity by taking that
    /// name. It is retired the moment a real administrator exists.</summary>
    public bool IsDefaultAdmin { get; set; }
}


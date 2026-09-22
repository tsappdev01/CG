using Microsoft.AspNetCore.Identity;

namespace CGTOOL.Web.Data;

// Add profile data for application users by adding properties to the ApplicationUser class
public class ApplicationUser : IdentityUser
{
    /// <summary>Web-relative path (under wwwroot) to the uploaded profile picture, e.g. "/uploads/profile-pictures/{id}.jpg". Null = no picture uploaded.</summary>
    public string? ProfilePicturePath { get; set; }
}


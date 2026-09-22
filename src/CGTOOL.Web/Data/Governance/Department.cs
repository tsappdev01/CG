using System.ComponentModel.DataAnnotations;

namespace CGTOOL.Web.Data.Governance;

public class Department
{
    public int Id { get; set; }

    [Required, MaxLength(20)]
    public string Code { get; set; } = string.Empty;

    [Required, MaxLength(120)]
    public string Name { get; set; } = string.Empty;

    public bool Active { get; set; } = true;

    public List<Member> Members { get; set; } = [];
}

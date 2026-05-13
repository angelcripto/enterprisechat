using System.ComponentModel.DataAnnotations;

namespace EnterpriseChat.Server.Data.Entities;

public sealed class Department
{
    public int Id { get; set; }

    [Required, MaxLength(128)]
    public string Name { get; set; } = null!;
}

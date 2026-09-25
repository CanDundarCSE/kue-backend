using System.ComponentModel.DataAnnotations;

namespace Kue.Api.Dtos.Admin;

public class UpdateUserRoleRequest
{
    [Required]
    public List<string> Roles { get; set; } = [];
}

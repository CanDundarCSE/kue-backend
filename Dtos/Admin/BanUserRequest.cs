using System.ComponentModel.DataAnnotations;

namespace Kue.Api.Dtos.Admin;

public class BanUserRequest
{
    public bool IsBanned { get; set; }

    [MaxLength(500, ErrorMessage = "Ban reason cannot exceed 500 characters")]
    public string? Reason { get; set; }
}

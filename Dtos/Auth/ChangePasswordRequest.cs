using System.ComponentModel.DataAnnotations;

namespace Kue.Api.Dtos.Auth;

public record ChangePasswordRequest(
    [Required(ErrorMessage = "Current password is required")]
    string CurrentPassword,

    [Required(ErrorMessage = "New password is required")]
    [MinLength(8, ErrorMessage = "Password must be at least 8 characters long")]
    [RegularExpression(@"^(?=.*[A-Z])(?=.*[^a-zA-Z0-9]).+$", ErrorMessage = "Password must contain at least one uppercase letter and one special character")]
    string NewPassword
);

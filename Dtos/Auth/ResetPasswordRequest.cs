using System.ComponentModel.DataAnnotations;

namespace Kue.Api.Dtos.Auth;

public record ResetPasswordRequest(
    [Required(ErrorMessage = "Email is required")]
    [EmailAddress(ErrorMessage = "Invalid email address")]
    string Email,

    [Required(ErrorMessage = "Reset token is required")]
    string Token,

    [Required(ErrorMessage = "New password is required")]
    [MinLength(8, ErrorMessage = "Password must be at least 8 characters long")]
    [RegularExpression(@"^(?=.*[A-Z])(?=.*[^a-zA-Z0-9]).+$", ErrorMessage = "Password must contain at least one uppercase letter and one special character")]
    string NewPassword
);

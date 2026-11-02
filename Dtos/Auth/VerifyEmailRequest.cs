using System.ComponentModel.DataAnnotations;

namespace Kue.Api.Dtos.Auth;

public record VerifyEmailRequest(
    [Required(ErrorMessage = "Email is required")]
    [EmailAddress(ErrorMessage = "Invalid email address")]
    string Email,

    [Required(ErrorMessage = "Verification code is required")]
    [StringLength(16, MinimumLength = 8, ErrorMessage = "Verification code is invalid")]
    string Code
);

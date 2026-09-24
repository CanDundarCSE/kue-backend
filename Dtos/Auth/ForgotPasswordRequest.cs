using System.ComponentModel.DataAnnotations;

namespace Kue.Api.Dtos.Auth;

public record ForgotPasswordRequest(
    [Required(ErrorMessage = "Email is required")]
    [EmailAddress(ErrorMessage = "Invalid email address")]
    string Email
);

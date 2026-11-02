using System.ComponentModel.DataAnnotations;

namespace Kue.Api.Dtos.Auth;

public record ResendVerificationRequest(
    [Required(ErrorMessage = "Email is required")]
    [EmailAddress(ErrorMessage = "Invalid email address")]
    string Email
);

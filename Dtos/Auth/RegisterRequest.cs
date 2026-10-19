using System.ComponentModel.DataAnnotations;

namespace Kue.Api.Dtos.Auth;

public record RegisterRequest(
    [Required (ErrorMessage = "Username is required")] 
    [StringLength(50, MinimumLength = 3, ErrorMessage = "Username must be between 3 and 50 characters")] 
    string Username,

    [Required (ErrorMessage = "Email is required")] 
    [EmailAddress (ErrorMessage = "Invalid email address")] 
    string Email,

    [Required (ErrorMessage = "Password is required")] 
    [MinLength(8, ErrorMessage = "Password must be at least 8 characters long")] 
    [RegularExpression(@"^(?=.*[A-Z])(?=.*[^a-zA-Z0-9]).+$", ErrorMessage = "Password must contain at least one uppercase letter and one special character")]
    string Password,

    // Explicit consent to the Privacy Policy and Terms of Service. Validated in
    // AuthService (not with a DataAnnotation) so the rejection comes back as the
    // same { message } shape the frontend already renders.
    bool IsTermsAccepted,

    // Version of the legal documents the client displayed (LEGAL_VERSION in the
    // web app). Recorded so we can prove which text was accepted; a stale value
    // is rejected so a cached consent screen cannot accept an outdated policy.
    string? TermsVersion = null
);
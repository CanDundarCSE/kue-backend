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
    string Password
);
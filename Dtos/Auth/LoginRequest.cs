using System.ComponentModel.DataAnnotations;

namespace Kue.Api.Dtos.Auth;

public record LoginRequest(
    [Required (ErrorMessage = "Email is required")] 
    [EmailAddress (ErrorMessage = "Invalid email address")] 
    string Email,
    [Required (ErrorMessage = "Password is required")] 
    [MinLength(8, ErrorMessage = "Password must be at least 8 characters long")] 
    string Password
);
using System.ComponentModel.DataAnnotations;

namespace Kue.Api.Dtos.Users;

public record UpdateProfileRequest(
    [StringLength(50, MinimumLength = 3, ErrorMessage = "Username must be between 3 and 50 characters")]
    string? Username,

    [EmailAddress(ErrorMessage = "Invalid email address")]
    string? Email,

    [MaxLength(500, ErrorMessage = "Bio must not exceed 500 characters")]
    string? Bio,

    bool? IsPrivate
);

using System.ComponentModel.DataAnnotations;

namespace Kue.Api.Dtos.Users;

public record UpdateProfileRequest(
    [StringLength(50, MinimumLength = 3, ErrorMessage = "Username must be between 3 and 50 characters")]
    string? Username,

    [MaxLength(500, ErrorMessage = "Avatar URL must not exceed 500 characters")]
    string? AvatarUrl
);

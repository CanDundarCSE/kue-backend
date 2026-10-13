using System.ComponentModel.DataAnnotations;

namespace Kue.Api.Dtos.Users;

public record UpdateProfileRequest(
    [StringLength(50, MinimumLength = 3, ErrorMessage = "Username must be between 3 and 50 characters")]
    string? Username,

    [EmailAddress(ErrorMessage = "Invalid email address")]
    string? Email,

    [MaxLength(500, ErrorMessage = "Bio must not exceed 500 characters")]
    string? Bio,

    // Avatar semantics: null = leave unchanged, empty string = clear (fall
    // back to the first-letter avatar), any other value must be one of the
    // ids in Constants/AvatarIds.
    [MaxLength(32)]
    string? AvatarId,

    bool? IsPrivate
);

using System.ComponentModel.DataAnnotations;

namespace Kue.Api.Dtos.Lists;

public class UpdateCustomListRequest
{
    [Required]
    [StringLength(150, MinimumLength = 1, ErrorMessage = "List name must be between 1 and 150 characters.")]
    public string Name { get; set; } = null!;

    [StringLength(1000, ErrorMessage = "Description cannot exceed 1000 characters.")]
    public string? Description { get; set; }

    /// <summary>
    /// "public", "friends" or "private". Omit to keep the current visibility.
    /// </summary>
    public string? Visibility { get; set; }

    /// <summary>
    /// Legacy visibility flag. Ignored when <see cref="Visibility"/> is set;
    /// kept so older clients keep working.
    /// </summary>
    public bool? IsPublic { get; set; }
}

using System.ComponentModel.DataAnnotations;

namespace Kue.Api.Dtos.Lists;

public class CreateCustomListRequest
{
    [Required]
    [StringLength(150, MinimumLength = 1, ErrorMessage = "List name must be between 1 and 150 characters.")]
    public string Name { get; set; } = null!;

    [StringLength(1000, ErrorMessage = "Description cannot exceed 1000 characters.")]
    public string? Description { get; set; }

    public bool IsPublic { get; set; } = true;
}

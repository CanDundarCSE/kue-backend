using System.ComponentModel.DataAnnotations;

namespace Kue.Api.Dtos.Reviews;

public class CreateReviewRequest
{
    [Required]
    [StringLength(5000, MinimumLength = 5, ErrorMessage = "Review content must be between 5 and 5000 characters.")]
    public string Content { get; set; } = null!;

    [Range(1, 10, ErrorMessage = "Rating must be an integer between 1 and 10.")]
    public int? Rating { get; set; }

    public bool ContainsSpoilers { get; set; } = false;
}

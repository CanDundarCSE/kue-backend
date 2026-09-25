using System.ComponentModel.DataAnnotations;

namespace Kue.Api.Dtos.Ratings;

public class SetRatingRequest
{
    [Required]
    [Range(1, 10, ErrorMessage = "Rating must be an integer between 1 and 10.")]
    public int Rating { get; set; }
}

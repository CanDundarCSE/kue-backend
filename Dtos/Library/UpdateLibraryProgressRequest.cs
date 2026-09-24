using System.ComponentModel.DataAnnotations;

namespace Kue.Api.Dtos.Library;

public class UpdateLibraryProgressRequest
{
    [Required]
    [Range(0, 100000, ErrorMessage = "Progress must be a non-negative number")]
    public int Progress { get; set; } // Current episode or chapter count
}
using System.ComponentModel.DataAnnotations;

namespace Kue.Api.Dtos.Library;

public class UpdateLibraryStatusRequest
{
    [Required]
    public string Status { get; set; } = null!; // "planning", "in_progress", "completed", "on_hold", "dropped"
}
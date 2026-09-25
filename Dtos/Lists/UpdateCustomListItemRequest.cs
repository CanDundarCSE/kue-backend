using System.ComponentModel.DataAnnotations;

namespace Kue.Api.Dtos.Lists;

public class UpdateCustomListItemRequest
{
    [StringLength(500, ErrorMessage = "Notes cannot exceed 500 characters.")]
    public string? Notes { get; set; }

    public int? Order { get; set; }
}

using System.ComponentModel.DataAnnotations;

namespace Kue.Api.Dtos.Lists;

public class ReorderCustomListRequest
{
    [Required]
    public List<int> MediaIds { get; set; } = [];
}

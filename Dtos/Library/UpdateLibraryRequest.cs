namespace Kue.Api.DTOs.Library;

public class UpdateLibraryRequest
{
    public string Status { get; set; } = null!;
    public int? Progress { get; set; }
    public bool? IsFavorite { get; set; }
}
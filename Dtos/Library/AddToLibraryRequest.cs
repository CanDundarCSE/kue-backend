namespace Kue.Api.DTOs.Library;

public class AddToLibraryRequest
{
    public string MediaType { get; set; } = null!;
    public string ExternalSource { get; set; } = null!;
    public string ExternalId { get; set; } = null!;
    public string Status { get; set; } = null!;
}
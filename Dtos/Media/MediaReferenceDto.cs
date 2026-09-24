namespace Kue.Api.Dtos.Media;

public class MediaReferenceDto
{
    public string MediaType { get; set; } = null!;
    public string ExternalSource { get; set; } = null!;
    public string ExternalId { get; set; } = null!;
}
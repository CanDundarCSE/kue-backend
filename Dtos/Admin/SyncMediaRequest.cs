namespace Kue.Api.DTOs.Admin;

public class SyncMediaRequest
{
    public string Source { get; set; } = null!;
    public string ExternalId { get; set; } = null!;
}
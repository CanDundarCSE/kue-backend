namespace Kue.Api.Dtos.Reviews;

public class SpoilerRangeDto
{
    /// <summary>Inclusive start offset (UTF-16 chars) into the review content.</summary>
    public int Start { get; set; }

    /// <summary>Exclusive end offset (UTF-16 chars) into the review content.</summary>
    public int End { get; set; }
}

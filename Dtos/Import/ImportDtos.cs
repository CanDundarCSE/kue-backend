namespace Kue.Api.Dtos.Import;

public class ImportFileDto
{
    public string FileName { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}

public class StartImportRequest
{
    public string FileName { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    /// <summary>
    /// Multi-file Trakt export: each entry is one .json file's text.
    /// No zip archives are accepted (zip-bomb risk); the client sends
    /// already-extracted .json texts. When present, Files is used and
    /// Content is ignored.
    /// </summary>
    public List<ImportFileDto>? Files { get; set; }
    /// <summary>Optional format override: mal-xml, anilist-json, trakt-json, csv.</summary>
    public string? Source { get; set; }
    /// <summary>CSV only: explicit header-name mapping (title, status, score, progress, type, platform).</summary>
    public Dictionary<string, string>? ColumnMapping { get; set; }
    /// <summary>CSV only: fallback media type when a row has none.</summary>
    public string? DefaultType { get; set; }
}

public class ResolveImportRequest
{
    /// <summary>Bulk default for undecided conflicts: keep or import.</summary>
    public string BulkDefault { get; set; } = "keep";
    public Dictionary<int, string>? Overrides { get; set; }
}

public class FieldConflictDto
{
    public string Field { get; set; } = string.Empty;
    public string? Local { get; set; }
    public string? Imported { get; set; }
}

public class ImportJobDto
{
    public int Id { get; set; }
    public string Source { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int TotalItems { get; set; }
    public int ProcessedItems { get; set; }
    public int ConflictCount { get; set; }
    public int ReadyCount { get; set; }
    public int AddedCount { get; set; }
    public int UpdatedCount { get; set; }
    public int SkippedCount { get; set; }
    public string? Error { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class ImportItemDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? MediaType { get; set; }
    public string? ImportedStatus { get; set; }
    public int? ImportedProgress { get; set; }
    public int? ImportedRating { get; set; }
    public string? ImportedPlatform { get; set; }
    public bool? ImportedFavorite { get; set; }
    public int? MatchedMediaId { get; set; }
    public List<FieldConflictDto> Conflicts { get; set; } = [];
    public string Status { get; set; } = string.Empty;
    public string? Decision { get; set; }
    public string? Error { get; set; }
}

public class ImportJobDetailsDto : ImportJobDto
{
    public List<ImportItemDto> Items { get; set; } = [];
}

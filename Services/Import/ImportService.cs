using System.Text.Json;
using Kue.Api.Data;
using Kue.Api.Dtos.Import;
using Kue.Api.Entities;
using Kue.Api.Services.Background;
using Kue.Api.Services.Import;
using Microsoft.EntityFrameworkCore;

namespace Kue.Api.Services.Import;

public interface IImportService
{
    Task<ImportJobDto> StartAsync(int userId, StartImportRequest request, CancellationToken ct = default);
    Task<List<ImportJobDto>> GetJobsAsync(int userId, CancellationToken ct = default);
    Task<ImportJobDetailsDto?> GetJobAsync(int userId, int jobId, CancellationToken ct = default);
    Task<ImportJobDto?> ResolveAsync(int userId, int jobId, ResolveImportRequest request, CancellationToken ct = default);
    Task<bool> CancelAsync(int userId, int jobId, CancellationToken ct = default);
}

public class ImportService : IImportService
{
    private const int MaxFileChars = 2_000_000;
    private const int MaxFiles = 50;
    private const int MaxTotalChars = 5_000_000;
    private static readonly HashSet<string> Formats = new(StringComparer.OrdinalIgnoreCase)
    {
        "mal-xml", "anilist-json", "trakt-json", "csv"
    };

    private readonly AppDbContext _context;
    private readonly IBackgroundTaskQueue _queue;
    private readonly ILogger<ImportService> _logger;

    public ImportService(AppDbContext context, IBackgroundTaskQueue queue, ILogger<ImportService> logger)
    {
        _context = context;
        _queue = queue;
        _logger = logger;
    }

    public async Task<ImportJobDto> StartAsync(int userId, StartImportRequest request, CancellationToken ct = default)
    {
        // Multi-file Trakt export: loose .json texts, never a zip archive.
        if (request.Files is { Count: > 0 })
        {
            return await StartMultiAsync(userId, request, ct);
        }

        var content = request.Content ?? string.Empty;
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException("Upload a non-empty export file.");
        }
        if (content.Length > MaxFileChars)
        {
            throw new InvalidOperationException("Export file is too large (2 MB max).");
        }

        var format = string.IsNullOrWhiteSpace(request.Source)
            ? ImportParsing.SniffFormat(request.FileName ?? string.Empty, content)
            : request.Source.Trim().ToLowerInvariant();
        if (!Formats.Contains(format))
        {
            throw new InvalidOperationException($"Unsupported import format '{request.Source}'.");
        }

        List<ParsedEntry> entries;
        try
        {
            entries = ImportParsing.Parse(format, content, request.ColumnMapping, request.DefaultType);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse {Format} import for user {UserId}.", format, userId);
            throw new InvalidOperationException($"Could not parse this file as {format}.");
        }

        if (entries.Count == 0)
        {
            throw new InvalidOperationException("No importable entries found in this file.");
        }
        if (entries.Count > 5000)
        {
            throw new InvalidOperationException("Too many entries (5000 max). Split the file and try again.");
        }

        var job = await CreateJobAsync(userId, entries, format, (request.FileName ?? string.Empty).Trim(), ct);
        return job;
    }

    /// <summary>
    /// Multi-file Trakt backup import. Files arrive as plain .json texts —
    /// zip archives are rejected outright (zip-bomb risk), the client sends
    /// already-extracted files.
    /// </summary>
    private async Task<ImportJobDto> StartMultiAsync(int userId, StartImportRequest request, CancellationToken ct)
    {
        var files = request.Files!;
        if (files.Count > MaxFiles)
        {
            throw new InvalidOperationException($"Too many files ({MaxFiles} max). Select the Trakt .json files only.");
        }

        var source = string.IsNullOrWhiteSpace(request.Source)
            ? "trakt-json"
            : request.Source.Trim().ToLowerInvariant();
        if (source != "trakt-json")
        {
            throw new InvalidOperationException("Uploading multiple files is only supported for Trakt exports.");
        }
        if (!ImportParsing.LooksLikeTraktExport(files.Select(f => f.FileName ?? string.Empty)))
        {
            throw new InvalidOperationException("These files don't look like a Trakt export.");
        }

        long total = 0;
        var payload = new List<(string FileName, string Content)>(files.Count);
        foreach (var file in files)
        {
            var name = (file.FileName ?? string.Empty).Trim();
            var fileContent = file.Content ?? string.Empty;
            if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || IsZipBytes(fileContent))
            {
                throw new InvalidOperationException("Zip archives aren't accepted. Unzip the Trakt export first and select the .json files.");
            }
            if (fileContent.Length > MaxFileChars)
            {
                throw new InvalidOperationException($"'{name}' is too large (2 MB max per file).");
            }
            total += fileContent.Length;
            if (total > MaxTotalChars)
            {
                throw new InvalidOperationException("These files are too large in total (5 MB max).");
            }
            payload.Add((name, fileContent));
        }

        List<ParsedEntry> entries;
        try
        {
            entries = ImportParsing.ParseTraktFiles(payload);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse multi-file Trakt import for user {UserId}.", userId);
            throw new InvalidOperationException("Could not parse these files as a Trakt export.");
        }

        if (entries.Count == 0)
        {
            throw new InvalidOperationException("No importable entries found in these files.");
        }
        if (entries.Count > 5000)
        {
            throw new InvalidOperationException("Too many entries (5000 max).");
        }

        var label = string.IsNullOrWhiteSpace(request.FileName)
            ? $"Trakt export ({files.Count} files)"
            : request.FileName.Trim();
        return await CreateJobAsync(userId, entries, "trakt-json", label, ct);
    }

    private static bool IsZipBytes(string content) =>
        content.Length >= 2 && content[0] == 'P' && content[1] == 'K';

    private async Task<ImportJobDto> CreateJobAsync(
        int userId, List<ParsedEntry> entries, string format, string fileName, CancellationToken ct)
    {
        var job = new ImportJob
        {
            UserId = userId,
            Source = format,
            FileName = fileName,
            Status = "pending",
            TotalItems = entries.Count,
            CreatedAt = DateTime.UtcNow
        };
        _context.ImportJobs.Add(job);
        await _context.SaveChangesAsync(ct);

        foreach (var entry in entries)
        {
            _context.ImportItems.Add(new ImportItem
            {
                JobId = job.Id,
                Title = entry.Title.Length > 300 ? entry.Title[..300] : entry.Title,
                MediaType = entry.MediaType,
                ExternalSource = entry.ExternalSource,
                ExternalId = entry.ExternalId,
                ImportedStatus = entry.Status,
                ImportedProgress = entry.Progress is >= 0 ? entry.Progress : null,
                ImportedRating = entry.Rating is >= 1 and <= 10 ? entry.Rating : null,
                ImportedPlatform = string.IsNullOrWhiteSpace(entry.Platform) ? null : entry.Platform.Trim(),
                ImportedFavorite = entry.IsFavorite,
                Status = "pending",
            });
        }
        await _context.SaveChangesAsync(ct);

        var jobId = job.Id;
        await _queue.EnqueueAsync((sp, token) =>
            sp.GetRequiredService<ImportProcessor>().ProcessAsync(jobId, token), ct);

        return await GetJobDtoAsync(userId, jobId, ct)
            ?? throw new InvalidOperationException("Import job could not be created.");
    }

    public async Task<List<ImportJobDto>> GetJobsAsync(int userId, CancellationToken ct = default)
    {
        return await _context.ImportJobs
            .AsNoTracking()
            .Where(j => j.UserId == userId)
            .OrderByDescending(j => j.CreatedAt)
            .Take(20)
            .Select(j => new ImportJobDto
            {
                Id = j.Id,
                Source = j.Source,
                FileName = j.FileName,
                Status = j.Status,
                TotalItems = j.TotalItems,
                ProcessedItems = j.ProcessedItems,
                ConflictCount = j.Items.Count(i => i.Status == "conflict"),
                ReadyCount = j.Items.Count(i => i.Status == "ready"),
                AddedCount = j.AddedCount,
                UpdatedCount = j.UpdatedCount,
                SkippedCount = j.SkippedCount,
                Error = j.Error,
                CreatedAt = j.CreatedAt,
                UpdatedAt = j.UpdatedAt,
            })
            .ToListAsync(ct);
    }

    public async Task<ImportJobDetailsDto?> GetJobAsync(int userId, int jobId, CancellationToken ct = default)
    {
        var job = await _context.ImportJobs
            .AsNoTracking()
            .Include(j => j.Items)
            .FirstOrDefaultAsync(j => j.Id == jobId && j.UserId == userId, ct);

        if (job is null) return null;

        var dto = MapJob(job);
        return new ImportJobDetailsDto
        {
            Id = dto.Id,
            Source = dto.Source,
            FileName = dto.FileName,
            Status = dto.Status,
            TotalItems = dto.TotalItems,
            ProcessedItems = dto.ProcessedItems,
            ConflictCount = job.Items.Count(i => i.Status == "conflict"),
            ReadyCount = job.Items.Count(i => i.Status == "ready"),
            AddedCount = dto.AddedCount,
            UpdatedCount = dto.UpdatedCount,
            SkippedCount = dto.SkippedCount,
            Error = dto.Error,
            CreatedAt = dto.CreatedAt,
            UpdatedAt = dto.UpdatedAt,
            Items = job.Items.OrderBy(i => i.Id).Select(MapItem).ToList(),
        };
    }

    public async Task<ImportJobDto?> ResolveAsync(
        int userId, int jobId, ResolveImportRequest request, CancellationToken ct = default)
    {
        var bulk = request.BulkDefault?.Trim().ToLowerInvariant() ?? "keep";
        if (bulk is not ("keep" or "import"))
        {
            throw new InvalidOperationException("bulkDefault must be 'keep' or 'import'.");
        }

        var job = await _context.ImportJobs
            .Include(j => j.Items)
            .FirstOrDefaultAsync(j => j.Id == jobId && j.UserId == userId, ct);

        if (job is null) return null;
        if (job.Status != "awaiting_resolution")
        {
            throw new InvalidOperationException("This import is not waiting for resolution.");
        }

        // Entries may have changed since processing (the user kept using the
        // app): re-diff conflicts first so converged items skip silently
        // instead of pointlessly updating.
        foreach (var item in job.Items.Where(i => i.Status == "conflict").ToList())
        {
            LibraryEntry? entry = null;
            if (item.MatchedMediaId.HasValue)
            {
                entry = await _context.LibraryEntries.AsNoTracking().FirstOrDefaultAsync(
                    e => e.UserId == userId && e.MediaId == item.MatchedMediaId.Value, ct);
            }
            if (entry is null) continue;

            var conflicts = ImportProcessor.BuildConflicts(
                entry.Status, entry.Progress, entry.Rating, entry.Platform, entry.IsFavorite, item);
            if (conflicts.Count == 0)
            {
                item.Status = "skipped";
                item.Decision = "skip";
                item.ConflictJson = null;
            }
            else
            {
                item.ConflictJson = JsonSerializer.Serialize(conflicts);
            }
        }

        var overrides = new Dictionary<int, string>();
        if (request.Overrides is not null)
        {
            foreach (var (itemId, decision) in request.Overrides)
            {
                var normalized = decision?.Trim().ToLowerInvariant();
                if (normalized is "keep" or "import" or "skip")
                {
                    overrides[itemId] = normalized;
                }
            }
        }

        foreach (var item in job.Items.Where(i => i.Status is "conflict" or "ready"))
        {
            if (overrides.TryGetValue(item.Id, out var overrideDecision))
            {
                item.Decision = overrideDecision;
            }
            else if (item.Status == "conflict")
            {
                item.Decision = bulk;
            }
            else
            {
                item.Decision ??= "import";
            }
        }

        job.Status = "applying";
        job.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);

        await _queue.EnqueueAsync((sp, token) =>
            sp.GetRequiredService<ImportProcessor>().ApplyAsync(jobId, token), ct);

        return MapJob(job);
    }

    public async Task<bool> CancelAsync(int userId, int jobId, CancellationToken ct = default)
    {
        var job = await _context.ImportJobs
            .FirstOrDefaultAsync(j => j.Id == jobId && j.UserId == userId, ct);

        if (job is null) return false;
        if (job.Status is "completed" or "failed" or "cancelled")
        {
            throw new InvalidOperationException("This import already finished.");
        }

        job.Status = "cancelled";
        job.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);
        return true;
    }

    private async Task<ImportJobDto?> GetJobDtoAsync(int userId, int jobId, CancellationToken ct)
    {
        var job = await _context.ImportJobs
            .AsNoTracking()
            .FirstOrDefaultAsync(j => j.Id == jobId && j.UserId == userId, ct);
        return job is null ? null : MapJob(job);
    }

    private static ImportJobDto MapJob(ImportJob job) => new()
    {
        Id = job.Id,
        Source = job.Source,
        FileName = job.FileName,
        Status = job.Status,
        TotalItems = job.TotalItems,
        ProcessedItems = job.ProcessedItems,
        AddedCount = job.AddedCount,
        UpdatedCount = job.UpdatedCount,
        SkippedCount = job.SkippedCount,
        Error = job.Error,
        CreatedAt = job.CreatedAt,
        UpdatedAt = job.UpdatedAt,
    };

    internal static ImportItemDto MapItem(ImportItem item)
    {
        List<FieldConflictDto> conflicts = [];
        if (!string.IsNullOrWhiteSpace(item.ConflictJson))
        {
            try
            {
                conflicts = JsonSerializer.Deserialize<List<FieldConflictDto>>(item.ConflictJson) ?? [];
            }
            catch (JsonException) { }
        }

        return new ImportItemDto
        {
            Id = item.Id,
            Title = item.Title,
            MediaType = item.MediaType,
            ImportedStatus = item.ImportedStatus,
            ImportedProgress = item.ImportedProgress,
            ImportedRating = item.ImportedRating,
            ImportedPlatform = item.ImportedPlatform,
            ImportedFavorite = item.ImportedFavorite,
            MatchedMediaId = item.MatchedMediaId,
            Conflicts = conflicts,
            Status = item.Status,
            Decision = item.Decision,
            Error = item.Error,
        };
    }
}

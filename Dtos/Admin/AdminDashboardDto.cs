namespace Kue.Api.Dtos.Admin;

public class AdminDashboardDto
{
    public AdminStatsDto Stats { get; set; } = new();
    public ServerHealthDto Server { get; set; } = new();
    public List<AdminUserDto> RecentUsers { get; set; } = [];
}

public class AdminStatsDto
{
    public int TotalUsers { get; set; }
    public int BannedUsers { get; set; }
    public int AdminUsers { get; set; }
    public int NewUsersLast7Days { get; set; }
    public int ActiveSessions { get; set; }

    public int TotalMedia { get; set; }
    public int DeletedMedia { get; set; }
    public int LibraryEntries { get; set; }
    public int Reviews { get; set; }
    public int CustomLists { get; set; }
    public int UserActivities { get; set; }
    public int ImportJobs { get; set; }
}

public class ServerHealthDto
{
    public string Status { get; set; } = "unknown";
    public DateTimeOffset CheckedAt { get; set; }

    public string Environment { get; set; } = null!;
    public string Runtime { get; set; } = null!;
    public string Version { get; set; } = null!;
    public string MachineName { get; set; } = null!;
    public int ProcessorCount { get; set; }
    public string? ProcessArchitecture { get; set; }
    public string? OsDescription { get; set; }

    public DateTimeOffset StartedAt { get; set; }
    public long UptimeSeconds { get; set; }
    public long WorkingSetBytes { get; set; }
    public long GcHeapBytes { get; set; }
    public int ThreadCount { get; set; }

    public bool DatabaseReachable { get; set; }
    public long DatabaseLatencyMs { get; set; }
    public string? DatabaseName { get; set; }
    public string? DatabaseProvider { get; set; }
    public long? DatabaseSizeBytes { get; set; }
    public int PendingMigrations { get; set; }
    public string? LatestMigration { get; set; }

    public RenderInfoDto? Render { get; set; }
    public IntegrationStatusDto Integrations { get; set; } = new();
}

public class RenderInfoDto
{
    public string? ServiceName { get; set; }
    public string? ServiceId { get; set; }
    public string? InstanceId { get; set; }
    public string? Region { get; set; }
    public string? Commit { get; set; }
    public string? Branch { get; set; }
    public string? Repo { get; set; }
    public string? ExternalHostname { get; set; }
}

public class IntegrationStatusDto
{
    public bool Tmdb { get; set; }
    public bool Igdb { get; set; }
    public bool Brevo { get; set; }
    public bool AniList { get; set; } = true;
}

using Kue.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kue.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Media> Media => Set<Media>();
    public DbSet<LibraryEntry> LibraryEntries => Set<LibraryEntry>();
    public DbSet<CustomList> CustomLists => Set<CustomList>();
    public DbSet<CustomListItem> CustomListItems => Set<CustomListItem>();
    public DbSet<Friendship> Friendships => Set<Friendship>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<UserActivity> UserActivities => Set<UserActivity>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<ImportJob> ImportJobs => Set<ImportJob>();
    public DbSet<ImportItem> ImportItems => Set<ImportItem>();
    public DbSet<MiniGameScore> MiniGameScores => Set<MiniGameScore>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(u => u.Id);
            entity.HasIndex(u => u.Email).IsUnique();
            entity.HasIndex(u => u.Username).IsUnique();
            entity.Property(u => u.Email).HasMaxLength(256).IsRequired();
            entity.Property(u => u.Username).HasMaxLength(50).IsRequired();
            entity.Property(u => u.KueId).HasMaxLength(20);
            entity.HasIndex(u => u.KueId).IsUnique();
            entity.Property(u => u.Bio).HasMaxLength(500);
            entity.Property(u => u.IsPrivate).HasDefaultValue(false);
            entity.Property(u => u.IsBanned).HasDefaultValue(false);
            entity.Property(u => u.IsTermsAccepted).HasDefaultValue(false);
            entity.Property(u => u.BanReason).HasMaxLength(500);
            entity.Property(u => u.PasswordResetToken).HasMaxLength(128);
        });

        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.HasKey(r => r.Id);
            entity.HasIndex(r => r.Token).IsUnique();
            entity.HasIndex(r => r.ReplacedByToken);

            // Covers the background cleanup predicate (expired OR long-revoked)
            // so purging never degrades to a full table scan.
            entity.HasIndex(r => new { r.ExpiresAt, r.IsRevoked, r.RevokedAtUtc });

            entity.Property(r => r.RevocationReason).HasMaxLength(50);
            entity.Property(r => r.ReplacedByToken).HasMaxLength(64);
            entity.Property(r => r.CreatedByIp).HasMaxLength(45);
            entity.Property(r => r.RevokedByIp).HasMaxLength(45);

            entity.HasOne(r => r.User)
                  .WithMany(u => u.RefreshTokens)
                  .HasForeignKey(r => r.UserId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Media>(entity =>
        {
            entity.HasKey(m => m.Id);

            // Composite unique index to avoid duplicate items from TMDb, AniList, IGDB
            entity.HasIndex(m => new { m.MediaType, m.ExternalSource, m.ExternalId }).IsUnique();

            // Fast filtering & search indices
            entity.HasIndex(m => m.MediaType);
            entity.HasIndex(m => m.Title);
            entity.HasIndex(m => m.Year);
            entity.HasIndex(m => m.Score);

            entity.Property(m => m.MediaType).HasMaxLength(50).IsRequired();
            entity.Property(m => m.ExternalSource).HasMaxLength(50).IsRequired();
            entity.Property(m => m.ExternalId).HasMaxLength(100).IsRequired();
            entity.Property(m => m.Title).HasMaxLength(300).IsRequired();
            entity.Property(m => m.OriginalTitle).HasMaxLength(300);
            entity.Property(m => m.UnitName).HasMaxLength(50);
            entity.Property(m => m.Developer).HasMaxLength(150);

            // Soft delete global filter
            entity.HasQueryFilter(m => !m.IsDeleted);
        });

        modelBuilder.Entity<LibraryEntry>(entity =>
        {
            entity.HasKey(e => e.Id);

            // One library entry per user per media
            entity.HasIndex(e => new { e.UserId, e.MediaId }).IsUnique();

            // Fast filtering by status and favorites
            entity.HasIndex(e => new { e.UserId, e.Status });
            entity.HasIndex(e => new { e.UserId, e.IsFavorite });

            entity.Property(e => e.Status).HasMaxLength(50).IsRequired();
            entity.Property(e => e.Platform).HasMaxLength(100);

            // Optimistic concurrency via PostgreSQL's xmin system column: overlapping
            // writes to the same entry (import worker vs. user request) fail with
            // DbUpdateConcurrencyException instead of silently losing an update.
            // No schema change: xmin is maintained by PostgreSQL itself.
            entity.Property<uint>("xmin").IsRowVersion();

            entity.HasOne(e => e.User)
                  .WithMany(u => u.LibraryEntries)
                  .HasForeignKey(e => e.UserId)
                  .OnDelete(DeleteBehavior.Cascade);

            // Restrict deletion of catalog media if user library records reference it
            entity.HasOne(e => e.Media)
                  .WithMany(m => m.LibraryEntries)
                  .HasForeignKey(e => e.MediaId)
                  .OnDelete(DeleteBehavior.Restrict);

            // Matching query filter so soft-deleted media doesn't leave orphaned library entries in queries
            entity.HasQueryFilter(e => !e.Media.IsDeleted);
        });

        modelBuilder.Entity<CustomList>(entity =>
        {
            entity.HasKey(l => l.Id);
            entity.Property(l => l.Name).HasMaxLength(150).IsRequired();
            entity.Property(l => l.Description).HasMaxLength(1000);

            entity.HasIndex(l => l.UserId);
            entity.HasIndex(l => l.IsPublic);
            entity.HasIndex(l => l.CreatedAt);

            entity.HasOne(l => l.User)
                  .WithMany(u => u.CustomLists)
                  .HasForeignKey(l => l.UserId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CustomListItem>(entity =>
        {
            entity.HasKey(i => i.Id);

            // One entry per media per list
            entity.HasIndex(i => new { i.ListId, i.MediaId }).IsUnique();
            entity.HasIndex(i => new { i.ListId, i.Order });

            entity.Property(i => i.Notes).HasMaxLength(500);

            entity.HasOne(i => i.List)
                  .WithMany(l => l.Items)
                  .HasForeignKey(i => i.ListId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(i => i.Media)
                  .WithMany(m => m.CustomListItems)
                  .HasForeignKey(i => i.MediaId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasQueryFilter(i => !i.Media.IsDeleted);
        });

        modelBuilder.Entity<Friendship>(entity =>
        {
            entity.HasKey(f => f.Id);
            entity.HasIndex(f => new { f.RequesterId, f.AddresseeId }).IsUnique();
            entity.HasIndex(f => new { f.AddresseeId, f.Status });
            entity.HasIndex(f => new { f.RequesterId, f.Status });

            entity.Property(f => f.Status).HasMaxLength(20).IsRequired();

            entity.HasOne(f => f.Requester)
                  .WithMany(u => u.SentFriendRequests)
                  .HasForeignKey(f => f.RequesterId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(f => f.Addressee)
                  .WithMany(u => u.ReceivedFriendRequests)
                  .HasForeignKey(f => f.AddresseeId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Notification>(entity =>
        {
            entity.HasKey(n => n.Id);
            entity.Property(n => n.Type).HasMaxLength(50).IsRequired();
            entity.Property(n => n.Title).HasMaxLength(150).IsRequired();
            entity.Property(n => n.Message).HasMaxLength(500).IsRequired();
            entity.Property(n => n.ReferenceType).HasMaxLength(50);
            entity.Property(n => n.IsRead).HasDefaultValue(false);

            entity.HasIndex(n => new { n.UserId, n.IsRead });
            entity.HasIndex(n => new { n.UserId, n.CreatedAt });

            entity.HasOne(n => n.User)
                  .WithMany(u => u.Notifications)
                  .HasForeignKey(n => n.UserId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(n => n.Actor)
                  .WithMany(u => u.TriggeredNotifications)
                  .HasForeignKey(n => n.ActorId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserActivity>(entity =>
        {
            entity.HasKey(a => a.Id);
            entity.Property(a => a.ActivityType).HasMaxLength(50).IsRequired();
            entity.Property(a => a.Action).HasMaxLength(100);
            entity.Property(a => a.DetailText).HasMaxLength(200);
            entity.Property(a => a.FormattedText).HasMaxLength(500);
            entity.Property(a => a.Status).HasMaxLength(50);
            entity.Property(a => a.UnitName).HasMaxLength(50);
            entity.Property(a => a.Platform).HasMaxLength(100);

            entity.HasIndex(a => a.UserId);
            entity.HasIndex(a => a.CreatedAt);
            entity.HasIndex(a => new { a.UserId, a.CreatedAt });

            entity.HasOne(a => a.User)
                  .WithMany(u => u.Activities)
                  .HasForeignKey(a => a.UserId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(a => a.Media)
                  .WithMany()
                  .HasForeignKey(a => a.MediaId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(a => a.CustomList)
                  .WithMany()
                  .HasForeignKey(a => a.CustomListId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Review>(entity =>
        {
            entity.HasKey(r => r.Id);

            entity.HasIndex(r => new { r.UserId, r.MediaId }).IsUnique();
            entity.HasIndex(r => r.MediaId);
            entity.HasIndex(r => r.CreatedAt);

            entity.Property(r => r.Content).HasMaxLength(255).IsRequired();
            entity.Property(r => r.ContainsSpoilers).HasDefaultValue(false);
            entity.Property(r => r.IsPublic).HasDefaultValue(true);

            // Spoiler spans live in a single jsonb column; reviews are never
            // queried by span, so no index is needed.
            entity.OwnsMany(r => r.Spoilers, b => b.ToJson());

            entity.HasOne(r => r.User)
                  .WithMany(u => u.Reviews)
                  .HasForeignKey(r => r.UserId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(r => r.Media)
                  .WithMany(m => m.Reviews)
                  .HasForeignKey(r => r.MediaId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasQueryFilter(r => !r.Media.IsDeleted);
        });

        modelBuilder.Entity<ImportJob>(entity =>
        {
            entity.HasKey(j => j.Id);
            entity.HasIndex(j => new { j.UserId, j.CreatedAt });
            entity.Property(j => j.Source).HasMaxLength(50).IsRequired();
            entity.Property(j => j.FileName).HasMaxLength(300);
            entity.Property(j => j.Status).HasMaxLength(50).IsRequired();
            entity.Property(j => j.Error).HasMaxLength(1000);

            entity.HasOne(j => j.User)
                  .WithMany()
                  .HasForeignKey(j => j.UserId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ImportItem>(entity =>
        {
            entity.HasKey(i => i.Id);
            entity.HasIndex(i => i.JobId);
            entity.Property(i => i.Title).HasMaxLength(300).IsRequired();
            entity.Property(i => i.MediaType).HasMaxLength(50);
            entity.Property(i => i.ExternalSource).HasMaxLength(50);
            entity.Property(i => i.ExternalId).HasMaxLength(100);
            entity.Property(i => i.ImportedStatus).HasMaxLength(50);
            entity.Property(i => i.ImportedPlatform).HasMaxLength(100);
            entity.Property(i => i.Status).HasMaxLength(50).IsRequired();
            entity.Property(i => i.Decision).HasMaxLength(20);
            entity.Property(i => i.Error).HasMaxLength(1000);

            entity.HasOne(i => i.Job)
                  .WithMany(j => j.Items)
                  .HasForeignKey(i => i.JobId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MiniGameScore>(entity =>
        {
            entity.HasKey(s => s.Id);
            entity.HasIndex(s => new { s.Game, s.Score });
            entity.HasIndex(s => new { s.UserId, s.CreatedAt });

            entity.Property(s => s.Game).HasMaxLength(50).IsRequired();

            entity.HasOne(s => s.User)
                  .WithMany(u => u.MiniGameScores)
                  .HasForeignKey(s => s.UserId)
                  .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
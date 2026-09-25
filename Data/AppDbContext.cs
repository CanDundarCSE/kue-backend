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
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<ReviewLike> ReviewLikes => Set<ReviewLike>();

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
            entity.Property(u => u.PasswordResetToken).HasMaxLength(128);
        });

        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.HasKey(r => r.Id);
            entity.HasIndex(r => r.Token).IsUnique();
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

        modelBuilder.Entity<Review>(entity =>
        {
            entity.HasKey(r => r.Id);

            // One review per user per media
            entity.HasIndex(r => new { r.UserId, r.MediaId }).IsUnique();

            entity.HasIndex(r => r.MediaId);
            entity.HasIndex(r => r.LikesCount);
            entity.HasIndex(r => r.CreatedAt);

            entity.Property(r => r.Content).HasMaxLength(5000).IsRequired();

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

        modelBuilder.Entity<ReviewLike>(entity =>
        {
            entity.HasKey(l => new { l.ReviewId, l.UserId });

            entity.HasOne(l => l.Review)
                  .WithMany(r => r.Likes)
                  .HasForeignKey(l => l.ReviewId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(l => l.User)
                  .WithMany(u => u.ReviewLikes)
                  .HasForeignKey(l => l.UserId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasQueryFilter(l => !l.Review.Media.IsDeleted);
        });
    }
}
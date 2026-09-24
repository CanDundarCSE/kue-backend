using Kue.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kue.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Media> Media => Set<Media>();

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
        });
    }
}
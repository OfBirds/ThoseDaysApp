using Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Api.Data;

public class AppDbContext : DbContext
{
    public DbSet<User> Users { get; set; }
    public DbSet<Cycle> Cycles { get; set; }
    public DbSet<Prediction> Predictions { get; set; }
    public DbSet<SystemSetting> SystemSettings { get; set; }

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Email).IsRequired();
            // PasswordHash is nullable: OIDC (CrimsonRaven) accounts have no local password.
            // ExternalSubject is the linked IdP subject — unique when set (filtered index).
            entity.HasIndex(e => e.ExternalSubject).IsUnique();
            entity.HasMany(e => e.Cycles)
                .WithOne(c => c.User)
                .HasForeignKey(c => c.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(e => e.Predictions)
                .WithOne(p => p.User)
                .HasForeignKey(p => p.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.Property(e => e.NotifyReleases).HasDefaultValue(true);
            // gen_random_uuid() backfills existing rows with unique tokens (PG15 core).
            entity.Property(e => e.UnsubscribeToken).HasDefaultValueSql("gen_random_uuid()");
            entity.HasIndex(e => e.UnsubscribeToken).IsUnique();
        });

        modelBuilder.Entity<Cycle>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.StartDate).IsRequired();
            entity.Property(e => e.DurationDays).IsRequired();
            entity.Property(e => e.Auto).HasDefaultValue(false);
        });

        modelBuilder.Entity<Prediction>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.PredictedStart).IsRequired();
            entity.Property(e => e.PredictedDuration).IsRequired();
        });

        modelBuilder.Entity<SystemSetting>(entity =>
        {
            entity.HasKey(e => e.Key);
        });
    }
}

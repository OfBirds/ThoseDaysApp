using Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Api.Data;

public class AppDbContext : DbContext
{
    public required DbSet<User> Users { get; set; }
    public required DbSet<Cycle> Cycles { get; set; }
    public required DbSet<Prediction> Predictions { get; set; }

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
            entity.Property(e => e.PasswordHash).IsRequired();
            entity.HasMany(e => e.Cycles)
                .WithOne(c => c.User)
                .HasForeignKey(c => c.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(e => e.Predictions)
                .WithOne(p => p.User)
                .HasForeignKey(p => p.UserId)
                .OnDelete(DeleteBehavior.Cascade);
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
    }
}

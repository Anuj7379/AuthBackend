using Microsoft.EntityFrameworkCore;
using AuthApi.Models;

namespace AuthApi.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasIndex(user => user.Email).IsUnique();

            entity.Property(user => user.Name)
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(user => user.Email)
                .HasMaxLength(255)
                .IsRequired();

            entity.Property(user => user.PasswordHash)
                .IsRequired();

            entity.Property(user => user.PhoneNumber)
                .HasMaxLength(32);

            entity.Property(user => user.Bio)
                .HasMaxLength(500);

            entity.Property(user => user.Address)
                .HasMaxLength(300);

            entity.Property(user => user.ProfileImageUrl)
                .HasMaxLength(500);
        });
    }
}

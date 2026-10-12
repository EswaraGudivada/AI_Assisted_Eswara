using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using UrlShortener.Core.Domain;
using UrlShortener.Core.Validation;

namespace UrlShortener.Infrastructure.Persistence;

public sealed class UrlShortenerDbContext(DbContextOptions<UrlShortenerDbContext> options) : DbContext(options)
{
    public DbSet<ShortLink> ShortLinks => Set<ShortLink>();

    public DbSet<ClickEvent> ClickEvents => Set<ClickEvent>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // SQLite has no native DateTimeOffset; the binary form keeps comparisons and ordering translatable.
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<DateTimeOffsetToBinaryConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ShortLink>(link =>
        {
            link.ToTable("short_links");
            link.HasKey(l => l.Id);
            link.Property(l => l.Code).HasMaxLength(ShortCodeFormat.MaxLength).IsRequired();
            link.HasIndex(l => l.Code).IsUnique();
            link.Property(l => l.OriginalUrl).HasMaxLength(2048).IsRequired();
            link.HasIndex(l => l.OriginalUrl);
            link.HasIndex(l => l.CreatedAt);
        });

        modelBuilder.Entity<ClickEvent>(click =>
        {
            click.ToTable("click_events");
            click.HasKey(c => c.Id);
            click.Property(c => c.Id).ValueGeneratedOnAdd();
            click.Property(c => c.Code).HasMaxLength(ShortCodeFormat.MaxLength).IsRequired();
            click.Property(c => c.Referrer).HasMaxLength(255);
            click.Property(c => c.UserAgent).HasMaxLength(512);
            click.HasIndex(c => new { c.Code, c.OccurredAt });
        });
    }
}

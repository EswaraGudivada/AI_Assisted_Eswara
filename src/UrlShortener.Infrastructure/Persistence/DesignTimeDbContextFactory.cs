using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace UrlShortener.Infrastructure.Persistence;

/// <summary>Lets <c>dotnet ef</c> build the model without starting the API host.</summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<UrlShortenerDbContext>
{
    public UrlShortenerDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<UrlShortenerDbContext>().UseSqlite("Data Source=urlshortener.db").Options);
}

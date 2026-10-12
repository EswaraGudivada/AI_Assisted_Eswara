using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace UrlShortener.IntegrationTests.Api;

/// <summary>Hosts the real API in memory. Uses in-memory storage unless <see cref="UseSqliteFile"/> is called.</summary>
public class ApiFactory : WebApplicationFactory<Program>
{
    private string? _databasePath;

    public Dictionary<string, string?> Settings { get; } = new()
    {
        ["Storage:Provider"] = "InMemory",
        ["Shortener:BaseUrl"] = "https://sho.rt",
        ["RateLimiting:CreatePermitsPerMinute"] = "10000",
        ["RateLimiting:RedirectPermitsPerMinute"] = "10000",
        ["ClickTracking:BatchSize"] = "10",
    };

    public ApiFactory UseSqliteFile()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"urlshortener-{Guid.NewGuid():N}.db");
        Settings["Storage:Provider"] = "Sqlite";
        Settings["Storage:ConnectionString"] = $"Data Source={_databasePath}";
        return this;
    }

    public HttpClient CreateNonRedirectingClient() =>
        CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(Settings));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && _databasePath is not null)
        {
            SqliteConnection.ClearAllPools();
            File.Delete(_databasePath);
        }
    }
}

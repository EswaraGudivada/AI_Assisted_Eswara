using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace UrlShortener.IntegrationTests.Api;

/// <summary>Hosts the real API in memory against its own SQLite file, created through the EF Core migrations.</summary>
public class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"urlshortener-{Guid.NewGuid():N}.db");

    public ApiFactory()
    {
        Settings = new()
        {
            ["ConnectionStrings:Default"] = $"Data Source={_databasePath}",
            ["OpenApi:Enabled"] = "true",
            ["Shortener:BaseUrl"] = "https://sho.rt",
            ["Security:AllowPrivateNetworkTargets"] = "false",
            ["Security:DeniedHosts:0"] = "evil.example",
            ["RateLimit:WritesPerMinutePerIp"] = "10000",
            ["RateLimit:ResolutionsPerMinutePerIp"] = "10000",
            ["Analytics:BatchSize"] = "10",
        };
    }

    public Dictionary<string, string?> Settings { get; }

    public string DatabasePath => _databasePath;

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
        if (disposing)
        {
            SqliteConnection.ClearAllPools();
            File.Delete(_databasePath);
        }
    }
}

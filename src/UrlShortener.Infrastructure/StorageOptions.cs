namespace UrlShortener.Infrastructure;

public enum StorageProvider
{
    InMemory,
    Sqlite,
}

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    public StorageProvider Provider { get; set; } = StorageProvider.InMemory;

    public string ConnectionString { get; set; } = "Data Source=urlshortener.db";
}

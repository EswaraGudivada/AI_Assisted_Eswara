using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using UrlShortener.Core.Services;

namespace UrlShortener.Infrastructure.Persistence;

/// <summary>
/// Databases created by earlier versions (via <c>EnsureCreated</c>) have the tables but no migrations history, so
/// <c>Migrate</c> would try to create them again and fail. This brings such a database to the initial migration's
/// schema, keeping its data, and records that migration as applied.
/// </summary>
internal static class LegacySchemaUpgrader
{
    public static async Task UpgradeIfNeededAsync(UrlShortenerDbContext db, CancellationToken cancellationToken)
    {
        // An empty history table is not proof of a migrated database: a failed earlier Migrate attempt leaves one behind.
        var history = db.GetService<IHistoryRepository>();
        if (!await TableExistsAsync(db, "short_links", cancellationToken)
            || (await history.ExistsAsync(cancellationToken) && (await history.GetAppliedMigrationsAsync(cancellationToken)).Count > 0))
        {
            return;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var clickColumns = await ColumnsAsync(db, "click_events", cancellationToken);
        if (clickColumns.Contains("Referrer"))
        {
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE click_events RENAME COLUMN Referrer TO RefererHost", cancellationToken);
        }

        if (clickColumns.Contains("UserAgent"))
        {
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE click_events RENAME COLUMN UserAgent TO UserAgentFamily", cancellationToken);
            await ReplaceUserAgentsWithFamiliesAsync(db, cancellationToken);
        }

        await db.Database.ExecuteSqlRawAsync(history.GetCreateIfNotExistsScript(), cancellationToken);
        var initialMigration = db.Database.GetMigrations().First();
        await db.Database.ExecuteSqlRawAsync(
            history.GetInsertScript(new HistoryRow(initialMigration, ProductInfo.GetVersion())),
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    // Old rows hold full user-agent strings; keep only the coarse family the current version stores.
    private static async Task ReplaceUserAgentsWithFamiliesAsync(UrlShortenerDbContext db, CancellationToken cancellationToken)
    {
        var agents = await QueryStringsAsync(
            db,
            "SELECT DISTINCT UserAgentFamily FROM click_events WHERE UserAgentFamily IS NOT NULL",
            cancellationToken);

        foreach (var agent in agents)
        {
            await db.Database.ExecuteSqlAsync(
                $"UPDATE click_events SET UserAgentFamily = {UserAgentClassifier.Classify(agent)} WHERE UserAgentFamily = {agent}",
                cancellationToken);
        }
    }

    private static async Task<bool> TableExistsAsync(UrlShortenerDbContext db, string table, CancellationToken cancellationToken) =>
        (await QueryStringsAsync(db, $"SELECT name FROM sqlite_master WHERE type = 'table' AND name = '{table}'", cancellationToken)).Count > 0;

    private static async Task<HashSet<string>> ColumnsAsync(UrlShortenerDbContext db, string table, CancellationToken cancellationToken) =>
        [.. await QueryStringsAsync(db, $"SELECT name FROM pragma_table_info('{table}')", cancellationToken)];

    private static async Task<List<string>> QueryStringsAsync(UrlShortenerDbContext db, string sql, CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        var opened = connection.State != System.Data.ConnectionState.Open;
        if (opened)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using DbCommand command = connection.CreateCommand();
            command.CommandText = sql;
            command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
            var values = new List<string>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                values.Add(reader.GetString(0));
            }

            return values;
        }
        finally
        {
            if (opened)
            {
                await connection.CloseAsync();
            }
        }
    }
}

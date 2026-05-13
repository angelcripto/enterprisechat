using Microsoft.EntityFrameworkCore;

namespace EnterpriseChat.Server.Data;

internal static class PersistenceExtensions
{
    public static IServiceCollection AddChatPersistence(
        this IServiceCollection services,
        IConfiguration _config)
    {
        // Resolve the connection string from DI at the point the DbContext is
        // built, NOT at registration time. This matters for tests where the
        // test fixture overrides ConnectionStrings:Sqlite via
        // ConfigureAppConfiguration — those callbacks run AFTER Program.cs
        // has already called AddChatPersistence, so reading the value here
        // would freeze in the un-overridden value.
        services.AddDbContextFactory<ChatDbContext>((sp, opts) =>
        {
            var cfg = sp.GetRequiredService<IConfiguration>();
            var connectionString = cfg.GetConnectionString("Sqlite")
                ?? throw new InvalidOperationException(
                    "Falta ConnectionStrings:Sqlite en la configuración del servidor.");
            opts.UseSqlite(connectionString);
        });

        return services;
    }

    /// <summary>
    /// Applies pending migrations and enables WAL journaling. Runs synchronously
    /// at startup so the host fails fast if the database is unreachable.
    /// </summary>
    public static async Task InitializeChatDatabaseAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        var factory = services.GetRequiredService<IDbContextFactory<ChatDbContext>>();
        await using var ctx = await factory.CreateDbContextAsync(cancellationToken);

        EnsureDataDirectoryExists(ctx.Database.GetConnectionString());

        await ctx.Database.MigrateAsync(cancellationToken);

        // WAL improves concurrent read+write throughput; safe to re-run.
        await ctx.Database.ExecuteSqlRawAsync(
            "PRAGMA journal_mode=WAL;", cancellationToken);
        await ctx.Database.ExecuteSqlRawAsync(
            "PRAGMA synchronous=NORMAL;", cancellationToken);
    }

    private static void EnsureDataDirectoryExists(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var builder = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(connectionString);
        var dataSource = builder.DataSource;
        if (string.IsNullOrWhiteSpace(dataSource) || dataSource.Equals(":memory:", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(dataSource));
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }
}

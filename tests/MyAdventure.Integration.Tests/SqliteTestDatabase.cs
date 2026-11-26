using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using MyAdventure.Infrastructure;
using MyAdventure.Infrastructure.Data;

namespace MyAdventure.Integration.Tests;

public sealed class SqliteTestDatabase : IDisposable
{
    public SqliteTestDatabase()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"myadventure-test-{Guid.NewGuid():N}.db");
        Services = new ServiceCollection().AddInfrastructure(Path).BuildServiceProvider();
    }

    public string Path { get; }

    public ServiceProvider Services { get; }

    public T Get<T>() where T : notnull => Services.GetRequiredService<T>();

    public Task InitializeAsync() => Get<DatabaseInitializer>().InitializeAsync(TestContext.Current.CancellationToken);

    public SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection($"Data Source={Path}");
        connection.Open();
        return connection;
    }

    public void Dispose()
    {
        Services.Dispose();
        SqliteConnection.ClearAllPools();
        try
        {
            File.Delete(Path);
        }
        catch (IOException)
        {
        }
    }
}

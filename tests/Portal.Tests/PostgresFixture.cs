using Npgsql;
using Testcontainers.PostgreSql;

namespace Portal.Tests;

/// <summary>Ein echter Postgres-Container; jeder Test holt sich mit <see cref="CreateDatabaseAsync"/> eine eigene Datenbank.</summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public async Task<string> CreateDatabaseAsync()
    {
        var name = $"t_{Guid.NewGuid():N}";
        await using (var admin = new NpgsqlConnection(_postgres.GetConnectionString()))
        {
            await admin.OpenAsync();
            await using var cmd = new NpgsqlCommand($"CREATE DATABASE {name}", admin);
            await cmd.ExecuteNonQueryAsync();
        }

        return new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString()) { Database = name }.ConnectionString;
    }

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();
}

[CollectionDefinition("infra")]
public sealed class InfraCollection : ICollectionFixture<KafkaFixture>, ICollectionFixture<PostgresFixture>;

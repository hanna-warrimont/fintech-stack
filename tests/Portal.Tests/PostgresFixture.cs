using System.Text;
using DotNet.Testcontainers.Containers;
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

    /// <summary>Verbindung als beliebige Rolle – für Tests, die Rechte prüfen (der Standard ist der Superuser).</summary>
    public string ConnectionString(string user, string password, string database) =>
        new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString()) { Username = user, Password = password, Database = database }
            .ConnectionString;

    /// <summary>Führt ein psql-Skript im Container aus (lokaler Socket, ohne Passwort). <paramref name="env"/>: <c>NAME=wert</c>.</summary>
    public async Task<ExecResult> PsqlAsync(string script, string user, params string[] env)
    {
        await _postgres.CopyAsync(Encoding.UTF8.GetBytes(script), "/tmp/script.sql");
        return await _postgres.ExecAsync(["env", .. env, "psql", "-U", user, "-d", "postgres", "-f", "/tmp/script.sql"]);
    }

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();
}

[CollectionDefinition("infra")]
public sealed class InfraCollection : ICollectionFixture<KafkaFixture>, ICollectionFixture<PostgresFixture>;

using Npgsql;

namespace Portal.Tests.Infra;

/// <summary>
/// infra/postgres/roles.sql (in Azure vom Job db-roles ausgeführt): Jede Dienst-Rolle darf in ihrer Datenbank Tabellen
/// anlegen und kommt in keine fremde hinein.
/// </summary>
[Collection("infra")]
public sealed class DatabaseRolesTests(PostgresFixture postgres)
{
    private static readonly (string Role, string Database)[] Services =
        [("instrument", "instrumentdb"), ("search", "searchdb"), ("history", "historydb"), ("keycloak", "keycloak")];

    [Fact]
    public async Task Each_service_role_uses_only_its_own_database()
    {
        await SetUpLikeAzureAsync();
        await RunRolesScriptAsync();
        await RunRolesScriptAsync(); // Redeploy: Ein zweiter Lauf darf nicht scheitern.

        foreach (var (role, database) in Services)
        {
            await using (var own = new NpgsqlConnection(postgres.ConnectionString(role, Password(role), database)))
            {
                await own.OpenAsync();
                await using var command = new NpgsqlCommand("CREATE TABLE probe (id int); INSERT INTO probe VALUES (1); DROP TABLE probe", own);
                await command.ExecuteNonQueryAsync();
            }

            foreach (var other in Services.Select(s => s.Database).Where(d => d != database))
            {
                await using var foreign = new NpgsqlConnection(postgres.ConnectionString(role, Password(role), other));
                var error = await Assert.ThrowsAsync<PostgresException>(() => foreign.OpenAsync());
                Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
            }
        }
    }

    /// <summary>Wie Azure Flexible Server: Der Admin ist kein Superuser, darf aber Rollen anlegen und besitzt die Datenbanken.</summary>
    private async Task SetUpLikeAzureAsync()
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString("postgres", "postgres", "postgres"));
        await connection.OpenAsync();
        await using (var role = new NpgsqlCommand("CREATE ROLE portaladmin LOGIN CREATEROLE", connection))
            await role.ExecuteNonQueryAsync();
        foreach (var (_, database) in Services)
        {
            await using var create = new NpgsqlCommand($"CREATE DATABASE {database} OWNER portaladmin", connection);
            await create.ExecuteNonQueryAsync();
        }
    }

    private async Task RunRolesScriptAsync()
    {
        var script = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "roles.sql"));
        var env = Services.Select(s => $"{s.Role.ToUpperInvariant()}_DB_PASSWORD={Password(s.Role)}").ToArray();
        var result = await postgres.PsqlAsync(script, "portaladmin", env);
        Assert.True(result.ExitCode == 0, result.Stderr);
    }

    private static string Password(string role) => $"pw-{role}";
}

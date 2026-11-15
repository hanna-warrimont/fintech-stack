using System.Data.Common;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace Portal.Tests.ServiceDefaults;

public sealed class HealthTests
{
    [Fact]
    public void Health_endpoints_exist_outside_development()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.AddDefaultHealthChecks();
        var app = builder.Build();
        app.MapDefaultEndpoints();

        var paths = ((IEndpointRouteBuilder)app).DataSources.SelectMany(d => d.Endpoints).OfType<RouteEndpoint>()
            .Select(e => e.RoutePattern.RawText);
        Assert.Equal(["/health", "/alive"], paths);
    }

    [Fact]
    public async Task Unreachable_database_fails_readiness_but_not_liveness()
    {
        var health = Build(NpgsqlDataSource.Create("Host=127.0.0.1;Port=1;Timeout=1"));

        var ready = await health.CheckHealthAsync();
        var alive = await health.CheckHealthAsync(r => r.Tags.Contains("live"));

        Assert.Equal(HealthStatus.Unhealthy, ready.Entries["db"].Status);
        Assert.Equal(HealthStatus.Healthy, alive.Status);
    }

    internal static HealthCheckService Build(DbDataSource db)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddDefaultHealthChecks();
        builder.AddDatabaseHealthCheck();
        builder.Services.AddSingleton(db);
        return builder.Build().Services.GetRequiredService<HealthCheckService>();
    }
}

[Collection("infra")]
public sealed class DatabaseHealthTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Reachable_database_is_ready()
    {
        var health = HealthTests.Build(NpgsqlDataSource.Create(await postgres.CreateDatabaseAsync()));

        Assert.Equal(HealthStatus.Healthy, (await health.CheckHealthAsync()).Status);
    }
}

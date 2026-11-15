using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

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
        var health = Build("Host=127.0.0.1;Port=1;Timeout=1");

        var ready = await health.CheckHealthAsync();
        var alive = await health.CheckHealthAsync(r => r.Tags.Contains("live"));

        Assert.Equal(HealthStatus.Unhealthy, ready.Status);
        Assert.Equal(HealthStatus.Healthy, alive.Status);
    }

    [Fact]
    public void Aspire_integration_also_registers_DbDataSource_for_the_application_queries()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration["ConnectionStrings:db"] = "Host=localhost;Database=x";
        builder.AddNpgsqlDataSource("db");

        // GetInstrumentQuery und ListCorporateActionsQuery hängen an DbDataSource, nicht an Npgsql (Clean Architecture).
        Assert.NotNull(builder.Build().Services.GetService<System.Data.Common.DbDataSource>());
    }

    /// <summary>Wie in Search, History und Instrument: ServiceDefaults plus die Aspire-Integration für Npgsql.</summary>
    internal static HealthCheckService Build(string connectionString)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration["ConnectionStrings:db"] = connectionString;
        builder.AddDefaultHealthChecks();
        builder.AddNpgsqlDataSource("db");
        return builder.Build().Services.GetRequiredService<HealthCheckService>();
    }
}

[Collection("infra")]
public sealed class DatabaseHealthTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Reachable_database_is_ready()
    {
        var health = HealthTests.Build(await postgres.CreateDatabaseAsync());

        Assert.Equal(HealthStatus.Healthy, (await health.CheckHealthAsync()).Status);
    }
}

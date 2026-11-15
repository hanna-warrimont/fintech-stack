using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Portal.Instruments.Application.Abstractions;

namespace Portal.Instruments.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Mit eigener Datenquelle aus einem Connection-String – für Tests und Werkzeuge ohne Aspire.</summary>
    public static IServiceCollection AddInstrumentInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddSingleton(_ => NpgsqlDataSource.Create(connectionString));
        // Lesende Queries in Application kennen nur die BCL-Abstraktion DbDataSource, nicht Postgres.
        services.AddSingleton<DbDataSource>(sp => sp.GetRequiredService<NpgsqlDataSource>());
        return services.AddInstrumentInfrastructure();
    }

    /// <summary>
    /// Erwartet eine registrierte <see cref="NpgsqlDataSource"/> (im Dienst: Aspire <c>AddNpgsqlDataSource</c>).
    /// EF bekommt bewusst NICHT Aspires <c>AddNpgsqlDbContext</c>: Dessen automatische Wiederholungen (Retry-Strategie)
    /// vertragen sich nicht mit eigenen Transaktionen wie im <see cref="CorporateActions.EffectivenessJob"/>.
    /// </summary>
    public static IServiceCollection AddInstrumentInfrastructure(this IServiceCollection services)
    {
        services.AddDbContext<InstrumentDbContext>((sp, o) => o.UseNpgsql(sp.GetRequiredService<NpgsqlDataSource>()));
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<InstrumentDbContext>());
        services.AddScoped<IInstrumentRepository, InstrumentRepository>();
        services.AddScoped<ICorporateActionRepository, CorporateActionRepository>();
        return services;
    }
}

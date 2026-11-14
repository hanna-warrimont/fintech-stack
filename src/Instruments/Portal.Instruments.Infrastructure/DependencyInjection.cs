using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Portal.Instruments.Application.Abstractions;

namespace Portal.Instruments.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInstrumentInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddSingleton(_ => NpgsqlDataSource.Create(connectionString));
        services.AddDbContext<InstrumentDbContext>((sp, o) => o.UseNpgsql(sp.GetRequiredService<NpgsqlDataSource>()));
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<InstrumentDbContext>());
        services.AddScoped<IInstrumentRepository, InstrumentRepository>();
        services.AddScoped<ICorporateActionRepository, CorporateActionRepository>();
        return services;
    }
}

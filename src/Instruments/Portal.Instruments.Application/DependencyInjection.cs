using Microsoft.Extensions.DependencyInjection;
using Portal.Instruments.Application.Abstractions;
using Portal.Instruments.Application.CorporateActions;
using Portal.Instruments.Application.Instruments;

namespace Portal.Instruments.Application;

public static class DependencyInjection
{
    /// <param name="leadTime">Mindestvorlauf des Stichtags bei der Freigabe (Standard 1 Minute, damit Splits live beobachtbar sind).</param>
    public static IServiceCollection AddInstrumentApplication(this IServiceCollection services, TimeSpan? leadTime = null) => services
        .AddScoped<CreateInstrumentHandler>()
        .AddScoped<RenameInstrumentHandler>()
        .AddSingleton<GetInstrumentQuery>()
        .AddScoped<CreateCorporateActionHandler>()
        .AddScoped(sp => new ChangeCorporateActionHandler(
            sp.GetRequiredService<ICorporateActionRepository>(), sp.GetRequiredService<IUnitOfWork>(),
            sp.GetRequiredService<TimeProvider>(), leadTime ?? TimeSpan.FromMinutes(1)))
        .AddSingleton<ListCorporateActionsQuery>();
}

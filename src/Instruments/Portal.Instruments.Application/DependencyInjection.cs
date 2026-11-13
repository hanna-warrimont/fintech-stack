using Microsoft.Extensions.DependencyInjection;
using Portal.Instruments.Application.Instruments;

namespace Portal.Instruments.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddInstrumentApplication(this IServiceCollection services) => services
        .AddScoped<CreateInstrumentHandler>()
        .AddScoped<RenameInstrumentHandler>()
        .AddSingleton<GetInstrumentQuery>();
}

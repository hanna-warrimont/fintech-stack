using Microsoft.EntityFrameworkCore;
using Portal.Instruments.Application.Instruments;
using Portal.Instruments.Domain.Instruments;
using Portal.Instruments.Infrastructure;

namespace Portal.Instruments.Api;

/// <summary>Start-Instrumente – angelegt über den normalen Handler, also auch über die Outbox nach Kafka.</summary>
internal static class SeedData
{
    private static readonly CreateInstrumentCommand[] Instruments =
    [
        new("DE0007164600", "716460", "SAP SE", InstrumentType.Share, "EUR"),
        new("DE0007236101", "723610", "Siemens AG", InstrumentType.Share, "EUR"),
        new("DE0008404005", "840400", "Allianz SE", InstrumentType.Share, "EUR"),
        new("IE00B4L5Y983", "A0RPWH", "iShares Core MSCI World UCITS ETF", InstrumentType.Etf, "EUR"),
    ];

    public static async Task EnsureAsync(IServiceProvider services, CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        if (await scope.ServiceProvider.GetRequiredService<InstrumentDbContext>().Instruments.AnyAsync(ct)) return;

        foreach (var command in Instruments)
        {
            await using var commandScope = services.CreateAsyncScope();
            var result = await commandScope.ServiceProvider.GetRequiredService<CreateInstrumentHandler>().Handle(command, ct);
            if (result.IsFailure) throw new InvalidOperationException($"Seed {command.Isin}: {result.Error.Code}");
        }
    }
}

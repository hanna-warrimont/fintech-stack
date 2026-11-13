using Portal.Instruments.Application.Instruments;

namespace Portal.Instruments.Api;

internal static class InstrumentEndpoints
{
    public sealed record RenameRequest(string Name);

    public static void MapInstrumentEndpoints(this IEndpointRouteBuilder app)
    {
        var instruments = app.MapGroup("/instruments");

        // ponytail: Schreib-Endpunkte ohne Login und nicht im BFF bis Runde 4 (Keycloak, Rollen)
        instruments.MapPost("/", async (CreateInstrumentCommand command, CreateInstrumentHandler handler, CancellationToken ct) =>
        {
            var result = await handler.Handle(command, ct);
            return result.IsSuccess
                ? Results.Created($"/instruments/{result.Value}", new { id = result.Value })
                : result.Error.ToProblem();
        });

        instruments.MapPut("/{id:guid}/name", async (Guid id, RenameRequest request, RenameInstrumentHandler handler, CancellationToken ct) =>
        {
            var result = await handler.Handle(new RenameInstrumentCommand(id, request.Name), ct);
            return result.IsSuccess ? Results.NoContent() : result.Error.ToProblem();
        });

        instruments.MapGet("/{id:guid}", async (Guid id, GetInstrumentQuery query, CancellationToken ct) =>
            await query.Handle(id, ct) is { } instrument ? Results.Ok(instrument) : Results.NotFound());
    }
}

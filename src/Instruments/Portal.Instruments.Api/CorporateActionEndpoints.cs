using System.Security.Claims;
using Portal.Instruments.Application.CorporateActions;
using Portal.Instruments.Domain.CorporateActions;

namespace Portal.Instruments.Api;

public static class CorporateActionEndpoints
{
    public sealed record CreateRequest(
        Guid InstrumentId, Domain.CorporateActions.CorporateActionType Type, int? SplitOld, int? SplitNew, string? NewIsin, DateTimeOffset EffectiveAt);

    public static void MapCorporateActionEndpoints(this IEndpointRouteBuilder app)
    {
        var actions = app.MapGroup("/corporate-actions");

        actions.MapPost("/", async (CreateRequest r, ClaimsPrincipal user, CreateCorporateActionHandler handler, CancellationToken ct) =>
        {
            var result = await handler.Handle(
                new CreateCorporateActionCommand(r.InstrumentId, r.Type, r.SplitOld, r.SplitNew, r.NewIsin, r.EffectiveAt, Actor(user)), ct);
            return result.IsSuccess ? Results.Created($"/corporate-actions/{result.Value}", new { id = result.Value }) : result.Error.ToProblem();
        }).RequireAuthorization(Policies.Pfleger);

        // Die Rolle steht sichtbar an jedem Schritt: Pfleger bearbeiten, Freigeber entscheiden.
        MapStep(actions, "submit", CorporateActionStep.Submit, Policies.Pfleger);
        MapStep(actions, "withdraw", CorporateActionStep.Withdraw, Policies.Pfleger);
        MapStep(actions, "discard", CorporateActionStep.Discard, Policies.Pfleger);
        MapStep(actions, "approve", CorporateActionStep.Approve, Policies.Freigeber);
        MapStep(actions, "reject", CorporateActionStep.Reject, Policies.Freigeber);
        MapStep(actions, "cancel", CorporateActionStep.Cancel, Policies.Freigeber);

        actions.MapGet("/", (ListCorporateActionsQuery query, CancellationToken ct) => query.Worklist(ct))
            .RequireAuthorization(Policies.PflegerOderFreigeber);

        // Öffentlich (PDP): nur entschiedene Fakten.
        app.MapGet("/instruments/{id:guid}/corporate-actions", (Guid id, ListCorporateActionsQuery query, CancellationToken ct) =>
            query.PublicForInstrument(id, ct));
    }

    private static void MapStep(RouteGroupBuilder group, string name, CorporateActionStep step, string policy) =>
        group.MapPost($"/{{id:guid}}/{name}", async (Guid id, ClaimsPrincipal user, ChangeCorporateActionHandler handler, CancellationToken ct) =>
        {
            var result = await handler.Handle(new ChangeCorporateActionCommand(id, step, Actor(user)), ct);
            return result.IsSuccess ? Results.NoContent() : result.Error.ToProblem();
        }).RequireAuthorization(policy);

    /// <summary>
    /// Vier-Augen vergleicht die Kennung (Claim <c>sub</c>), der Name (<c>preferred_username</c> über NameClaimType) ist
    /// nur zur Anzeige. Fehlt einer der Claims, ist das Token falsch konfiguriert – dann lieber 500 als eine Regel ohne Grundlage.
    /// </summary>
    private static Actor Actor(ClaimsPrincipal user) => new(
        user.FindFirstValue("sub") ?? throw new InvalidOperationException("Angemeldeter Nutzer ohne sub-Claim."),
        user.Identity?.Name ?? throw new InvalidOperationException("Angemeldeter Nutzer ohne Namen."));
}

public static class Policies
{
    public const string Pfleger = "pfleger";
    public const string Freigeber = "freigeber";
    public const string PflegerOderFreigeber = "pflegerOderFreigeber";
}

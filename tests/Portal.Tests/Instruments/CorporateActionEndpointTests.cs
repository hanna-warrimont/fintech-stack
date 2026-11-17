using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Portal.Instruments.Api;
using Portal.Instruments.Application;
using Portal.Instruments.Infrastructure;

namespace Portal.Tests.Instruments;

public sealed class CorporateActionEndpointTests
{
    /// <summary>Bestandsliste: Welche Policy hängt an welchem Endpunkt? Ein neuer Endpunkt muss hier bewusst eingetragen werden.</summary>
    [Fact]
    public void Write_endpoints_require_expected_role()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddInstrumentApplication().AddInstrumentInfrastructure("Host=unused"); // nur für die Parameter-Erkennung
        var app = builder.Build();
        app.MapInstrumentEndpoints();
        app.MapCorporateActionEndpoints();

        var policies = ((IEndpointRouteBuilder)app).DataSources.SelectMany(d => d.Endpoints).OfType<RouteEndpoint>()
            .ToDictionary(
                e => $"{e.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Single()} {e.RoutePattern.RawText!.TrimEnd('/')}",
                e => string.Join(",", e.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(a => a.Policy)));

        Assert.Equal(new Dictionary<string, string>
        {
            ["POST /instruments"] = "pfleger",
            ["PUT /instruments/{id:guid}/name"] = "pfleger",
            ["GET /instruments/{id:guid}"] = "",
            ["GET /instruments/{id:guid}/corporate-actions"] = "",
            ["POST /corporate-actions"] = "pfleger",
            ["POST /corporate-actions/{id:guid}/submit"] = "pfleger",
            ["POST /corporate-actions/{id:guid}/withdraw"] = "pfleger",
            ["POST /corporate-actions/{id:guid}/discard"] = "pfleger",
            ["POST /corporate-actions/{id:guid}/approve"] = "freigeber",
            ["POST /corporate-actions/{id:guid}/reject"] = "freigeber",
            ["POST /corporate-actions/{id:guid}/cancel"] = "freigeber",
            ["GET /corporate-actions"] = "pflegerOderFreigeber",
        }, policies);
    }

    /// <summary>Was die Policy-Namen oben bedeuten: die Rolle aus dem Claim <c>roles</c> (RoleClaimType wie in Program.cs).</summary>
    [Theory]
    [InlineData(Policies.Pfleger, "pfleger", true)]
    [InlineData(Policies.Pfleger, "freigeber", false)]
    [InlineData(Policies.Freigeber, "freigeber", true)]
    [InlineData(Policies.Freigeber, "pfleger", false)]
    [InlineData(Policies.PflegerOderFreigeber, "pfleger", true)]
    [InlineData(Policies.PflegerOderFreigeber, "freigeber", true)]
    [InlineData(Policies.PflegerOderFreigeber, "leser", false)]
    public async Task Policy_requires_role(string policy, string role, bool allowed)
    {
        using var sp = new ServiceCollection().AddLogging().AddAuthorizationBuilder().AddPortalPolicies().Services.BuildServiceProvider();
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim("roles", role)], "Bearer", "preferred_username", "roles"));

        Assert.Equal(allowed, (await sp.GetRequiredService<IAuthorizationService>().AuthorizeAsync(user, policy)).Succeeded);
    }
}

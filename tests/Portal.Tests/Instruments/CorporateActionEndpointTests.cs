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
}

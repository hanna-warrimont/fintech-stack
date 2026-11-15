using System.Diagnostics;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Portal.Bff;

namespace Portal.Tests.Bff;

/// <summary>PDP-Aggregation gegen einen gespielten Instrument-Dienst, mit dem echten Resilience-Handler.</summary>
public sealed class BffPdpTests
{
    private static readonly Guid Id = Guid.NewGuid();

    [Fact]
    public async Task Both_parts_are_combined()
    {
        var result = await Pdp((request, _) => Json(request.RequestUri!.AbsolutePath.EndsWith("/corporate-actions", StringComparison.Ordinal) ? "[]" : "{}"));

        Assert.Equal(StatusCodes.Status200OK, Status(result));
    }

    [Fact]
    public async Task Unknown_instrument_is_404()
    {
        var result = await Pdp((request, _) => request.RequestUri!.AbsolutePath.EndsWith("/corporate-actions", StringComparison.Ordinal)
            ? Json("[]")
            : Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));

        Assert.Equal(StatusCodes.Status404NotFound, Status(result));
    }

    [Fact]
    public async Task Unexpected_answer_is_502_problem_not_500()
    {
        // 400 wiederholt der Resilience-Handler nicht – der Fehler kommt sofort an.
        var result = await Pdp((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)));

        Assert.Equal(StatusCodes.Status502BadGateway, Assert.IsType<ProblemHttpResult>(result).StatusCode);
    }

    [Fact]
    public async Task Hanging_service_is_504_after_about_5_seconds_not_30()
    {
        var clock = Stopwatch.StartNew();

        // Antwortet nie; erst das PDP-Limit bricht ab (über das Token des Aufrufs).
        var result = await Pdp(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            throw new UnreachableException();
        });

        Assert.Equal(StatusCodes.Status504GatewayTimeout, Assert.IsType<ProblemHttpResult>(result).StatusCode);
        Assert.InRange(clock.Elapsed, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(8));
    }

    private static async Task<IResult> Pdp(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> backend)
    {
        var services = new ServiceCollection();
        services.ConfigureHttpClientDefaults(http => http.AddStandardResilienceHandler()); // wie in ServiceDefaults
        services.AddHttpClient(BffEndpoints.InstrumentClient, c => c.BaseAddress = new Uri("http://instrument"))
            .ConfigurePrimaryHttpMessageHandler(() => new Backend(backend));
        await using var provider = services.BuildServiceProvider();
        return await BffEndpoints.Pdp(Id, provider.GetRequiredService<IHttpClientFactory>(),
            NullLogger<BffEndpoints.PdpLog>.Instance, CancellationToken.None);
    }

    private static Task<HttpResponseMessage> Json(string body) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });

    private static int? Status(IResult result) => ((IStatusCodeHttpResult)result).StatusCode;

    private sealed class Backend(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => respond(request, ct);
    }
}

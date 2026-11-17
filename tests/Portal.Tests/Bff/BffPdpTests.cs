using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Portal.Bff;

namespace Portal.Tests.Bff;

/// <summary>PDP-Aggregation gegen einen gespielten Instrument-Dienst, mit den echten ServiceDefaults (Resilience, Service Discovery).</summary>
public sealed class BffPdpTests
{
    private static readonly Guid Id = Guid.NewGuid();

    [Fact]
    public async Task Both_parts_are_combined()
    {
        var result = await Pdp((request, _) => Json(request.RequestUri!.AbsolutePath.EndsWith("/corporate-actions", StringComparison.Ordinal)
            ? """[{"id":"ca"}]"""
            : """{"name":"SAP SE"}"""));

        Assert.Equal(StatusCodes.Status200OK, Status(result));
        var body = JsonNode.Parse(JsonSerializer.Serialize(((IValueHttpResult)result).Value, JsonSerializerOptions.Web));
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse("""{"instrument":{"name":"SAP SE"},"corporateActions":[{"id":"ca"}]}"""), body), body!.ToJsonString());
    }

    [Fact]
    public async Task Unknown_instrument_is_404()
    {
        // Wie der echte Dienst: Die öffentliche Liste ist für ein unbekanntes Instrument leer, nicht 404.
        var result = await Pdp((request, _) => request.RequestUri!.AbsolutePath.EndsWith("/corporate-actions", StringComparison.Ordinal)
            ? Json("[]")
            : Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));

        Assert.Equal(StatusCodes.Status404NotFound, Status(result));
    }

    [Fact]
    public async Task Non_retryable_error_answer_is_502_problem_not_500()
    {
        // 400 wiederholt der Resilience-Handler nicht – der Fehler kommt sofort an.
        var result = await Pdp((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)));

        Assert.Equal(StatusCodes.Status502BadGateway, Assert.IsType<ProblemHttpResult>(result).StatusCode);
    }

    [Fact]
    public async Task Unreachable_service_is_retried_until_the_pdp_limit_then_504()
    {
        // Der echte Ausfall: Verbindung abgelehnt. Der Standard-Handler wiederholt mit Backoff (2 s, 4 s, …) –
        // das PDP-Limit von 5 s greift vorher, also 504 statt 502.
        var calls = 0;
        var clock = Stopwatch.StartNew();

        var result = await Pdp((_, _) =>
        {
            Interlocked.Increment(ref calls);
            throw new HttpRequestException("Connection refused", new System.Net.Sockets.SocketException(61));
        });

        Assert.Equal(StatusCodes.Status504GatewayTimeout, Assert.IsType<ProblemHttpResult>(result).StatusCode);
        Assert.InRange(clock.Elapsed, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(8));
        Assert.True(calls > 2, $"{calls} Aufrufe – erwartet: wiederholt");
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
        var builder = Host.CreateApplicationBuilder();
        builder.AddServiceDefaults(); // wie in src/Bff/Program.cs
        builder.Services.AddHttpClient(BffEndpoints.InstrumentClient, c => c.BaseAddress = new Uri("http://instrument"))
            .ConfigurePrimaryHttpMessageHandler(() => new Backend(backend));
        using var host = builder.Build();
        return await BffEndpoints.Pdp(Id, host.Services.GetRequiredService<IHttpClientFactory>(),
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

using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;

namespace Portal.Tests.ServiceDefaults;

/// <summary>ServiceDefaults wie in jedem Dienst: Ein Endpunkt, der wirft, liefert in Production ProblemDetails.</summary>
public sealed class ProblemDetailsTests
{
    [Fact]
    public async Task Unhandled_exception_becomes_problem_details_outside_development()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.WebHost.UseUrls("http://127.0.0.1:0"); // freier Port, echter Kestrel statt TestServer
        builder.AddServiceDefaults();
        await using var app = builder.Build();
        app.MapGet("/boom", (Func<string>)(() => throw new InvalidOperationException("geheimes Detail")));
        app.MapDefaultEndpoints();
        await app.StartAsync();

        var address = app.Urls.Single(); // nach dem Start mit dem tatsächlich vergebenen Port
        using var http = new HttpClient();
        using var response = await http.GetAsync($"{address}/boom");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal(StatusCodes.Status500InternalServerError, problem!.Status);
        Assert.DoesNotContain("geheimes Detail", await response.Content.ReadAsStringAsync()); // in Production keine Interna
    }
}

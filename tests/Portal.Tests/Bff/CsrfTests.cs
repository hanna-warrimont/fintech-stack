using Microsoft.AspNetCore.Http;
using Portal.Bff;

namespace Portal.Tests.Bff;

public sealed class CsrfTests
{
    private static HttpRequest Request(string method, string? csrf = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        if (csrf is not null) context.Request.Headers["X-CSRF"] = csrf;
        return context.Request;
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("HEAD")]
    [InlineData("OPTIONS")]
    public void Safe_methods_are_allowed(string method) => Assert.True(Csrf.IsAllowed(Request(method)));

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public void Unsafe_request_without_csrf_header_is_rejected(string method) => Assert.False(Csrf.IsAllowed(Request(method)));

    [Fact]
    public void Unsafe_request_with_csrf_header_is_allowed() => Assert.True(Csrf.IsAllowed(Request("POST", "1")));

    [Theory]
    [InlineData(null, "/")]
    [InlineData("/kapitalmassnahmen", "/kapitalmassnahmen")]
    [InlineData("//evil.example", "/")]
    [InlineData("https://evil.example", "/")]
    [InlineData("/\\evil.example", "/")]
    public void Return_url_must_be_local(string? returnUrl, string expected) => Assert.Equal(expected, BffEndpoints.SafeReturnUrl(returnUrl));
}

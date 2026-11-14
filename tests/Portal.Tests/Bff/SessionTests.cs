using Microsoft.AspNetCore.Authentication;
using Portal.Bff;

namespace Portal.Tests.Bff;

public sealed class SessionTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);

    private static AuthenticationProperties WithExpiry(DateTimeOffset? expiresAt)
    {
        var properties = new AuthenticationProperties();
        if (expiresAt is { } at) properties.StoreTokens([new AuthenticationToken { Name = "expires_at", Value = at.ToString("o") }]);
        return properties;
    }

    [Fact]
    public void Session_with_expired_access_token_is_expired() =>
        Assert.True(Session.IsExpired(WithExpiry(Now.AddSeconds(-1)), Now));

    [Fact]
    public void Session_with_valid_access_token_is_not_expired() =>
        Assert.False(Session.IsExpired(WithExpiry(Now.AddMinutes(5)), Now));

    [Fact]
    public void Session_without_token_expiry_is_expired() => Assert.True(Session.IsExpired(WithExpiry(null), Now));
}

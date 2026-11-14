using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Caching.Memory;

namespace Portal.Bff;

/// <summary>
/// Serverseitige Session: Das Cookie enthält nur einen Schlüssel, Identität und Tokens (Access, Refresh, ID) bleiben
/// im BFF. Der Browser bekommt nie ein Token zu sehen – auch kein JavaScript, das per XSS eingeschleust wurde.
/// </summary>
// ponytail: Sitzungen im Speicher – nach BFF-Neustart neu anmelden, und nur eine BFF-Instanz; Ausbau IDistributedCache
public sealed class MemoryTicketStore : ITicketStore
{
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());

    public async Task<string> StoreAsync(AuthenticationTicket ticket)
    {
        var key = Guid.NewGuid().ToString("N");
        await RenewAsync(key, ticket);
        return key;
    }

    public Task RenewAsync(string key, AuthenticationTicket ticket)
    {
        _cache.Set(key, ticket, new MemoryCacheEntryOptions { AbsoluteExpiration = ticket.Properties.ExpiresUtc ?? DateTimeOffset.UtcNow.AddHours(8) });
        return Task.CompletedTask;
    }

    public Task<AuthenticationTicket?> RetrieveAsync(string key) => Task.FromResult(_cache.Get<AuthenticationTicket>(key));

    public Task RemoveAsync(string key)
    {
        _cache.Remove(key);
        return Task.CompletedTask;
    }
}

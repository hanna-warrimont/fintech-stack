using Portal.Instruments.Domain.CorporateActions;

namespace Portal.Tests.Instruments;

/// <summary>Die Testnutzer aus dem Realm-Import, mit fester Kennung (sub) wie in Keycloak.</summary>
public static class Users
{
    public static readonly Actor Pia = new("8a3c6f2e-0001-4000-8000-000000000001", "pia");
    public static readonly Actor Fred = new("8a3c6f2e-0002-4000-8000-000000000002", "fred");
    public static readonly Actor Frieda = new("8a3c6f2e-0003-4000-8000-000000000003", "frieda");
}

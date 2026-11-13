using Portal.Messaging.Contracts;
using Portal.Simulator;

namespace Portal.Tests.Simulator;

public sealed class InstrumentCatalogTests
{
    private static readonly Guid Sap = Guid.NewGuid();

    private static InstrumentSnapshot S(string name, int revision, string status = "Active") =>
        new(Sap, "DE0007164600", "716460", name, "Share", "EUR", status, revision);

    [Fact]
    public void Apply_adds_active_instrument()
    {
        var catalog = new InstrumentCatalog();
        catalog.Apply(S("SAP SE", 1));
        Assert.Equal("SAP SE", Assert.Single(catalog.Active).Name);
    }

    [Fact]
    public void Newer_revision_replaces()
    {
        var catalog = new InstrumentCatalog();
        catalog.Apply(S("SAP SE", 1));
        catalog.Apply(S("SAP", 2));
        Assert.Equal("SAP", Assert.Single(catalog.Active).Name);
    }

    [Fact]
    public void Older_revision_is_ignored()
    {
        var catalog = new InstrumentCatalog();
        catalog.Apply(S("SAP", 2));
        catalog.Apply(S("SAP SE", 1));
        Assert.Equal("SAP", Assert.Single(catalog.Active).Name);
    }

    [Fact]
    public void Delisted_is_not_active()
    {
        var catalog = new InstrumentCatalog();
        catalog.Apply(S("SAP SE", 1));
        catalog.Apply(S("SAP SE", 2, "Delisted"));
        Assert.Empty(catalog.Active);
    }
}

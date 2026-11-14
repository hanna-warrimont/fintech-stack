using Portal.Messaging.Contracts;
using Portal.Simulator;

namespace Portal.Tests.Simulator;

public sealed class PriceBookTests
{
    private static readonly InstrumentSnapshot Sap = new(Guid.NewGuid(), "DE0007164600", null, "SAP SE", "Share", "EUR", "Active", 1);

    [Fact]
    public void Split_scales_current_price()
    {
        var book = new PriceBook();
        var rng = new Random(42);
        var before = book.Next(Sap, rng);

        book.ApplySplit(Sap.Id, 0.25m);
        var after = book.Next(Sap, rng);

        Assert.InRange(after, before * 0.25m * 0.99m, before * 0.25m * 1.01m);
    }

    [Fact]
    public void Split_before_first_price_is_ignored()
    {
        var book = new PriceBook();
        book.ApplySplit(Sap.Id, 0.25m);

        Assert.InRange(book.Next(Sap, new Random(1)), 238m, 242m); // Startkurs SAP 240
    }
}

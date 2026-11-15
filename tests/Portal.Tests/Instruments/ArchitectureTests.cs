using System.Reflection;
using Portal.Instruments.Application.Instruments;
using Portal.Instruments.Domain.Instruments;
using Portal.Instruments.Infrastructure;

namespace Portal.Tests.Instruments;

/// <summary>
/// Clean Architecture: Abhängigkeiten zeigen nach innen. Geprüft über die Referenzen der kompilierten Assemblies –
/// ohne Zusatz-Library. (Typ-Anker statt Program: mehrere referenzierte Web-Projekte haben ein Program.)
/// </summary>
public sealed class ArchitectureTests
{
    private static readonly Assembly Domain = typeof(Instrument).Assembly;
    private static readonly Assembly Application = typeof(CreateInstrumentHandler).Assembly;
    private static readonly Assembly Infrastructure = typeof(InstrumentDbContext).Assembly;

    [Fact]
    public void Domain_depends_on_nothing_outside_the_bcl() =>
        AssertNoReference(Domain, "Portal.", "Microsoft.EntityFrameworkCore", "Npgsql", "Confluent", "Dapper");

    [Fact]
    public void Application_does_not_depend_on_infrastructure_api_or_drivers() =>
        // Dapper ist erlaubt (Lese-Seite wie im Kurs), Treiber nicht: Application kennt nur System.Data.Common.DbDataSource.
        AssertNoReference(Application, "Portal.Instruments.Infrastructure", "Portal.Instruments.Api", "Microsoft.EntityFrameworkCore",
            "Npgsql", "Confluent");

    [Fact]
    public void Infrastructure_does_not_depend_on_api() => AssertNoReference(Infrastructure, "Portal.Instruments.Api");

    private static void AssertNoReference(Assembly assembly, params string[] forbiddenPrefixes)
    {
        var violations = assembly.GetReferencedAssemblies()
            .Select(r => r.Name!)
            .Where(name => forbiddenPrefixes.Any(name.StartsWith))
            .ToList();
        Assert.True(violations.Count == 0, $"{assembly.GetName().Name} referenziert {string.Join(", ", violations)}");
    }
}

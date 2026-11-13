using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Portal.Instruments.Infrastructure;

/// <summary>Nur für <c>dotnet ef migrations</c>: braucht keine echte Datenbank.</summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<InstrumentDbContext>
{
    public InstrumentDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<InstrumentDbContext>().UseNpgsql("Host=localhost;Database=design").Options);
}

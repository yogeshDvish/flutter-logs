using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Arovyx.SealedTrace.Infrastructure.Persistence;

/// <summary>
/// Design-time factory for <c>dotnet ef</c>. Runtime configuration still comes
/// from the API host; this only supplies a local SQL Server target for migrations.
/// </summary>
public sealed class SealedTraceDbContextFactory : IDesignTimeDbContextFactory<SealedTraceDbContext>
{
    public SealedTraceDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? "Server=.\\SQLEXPRESS;Database=SealedTrace;Trusted_Connection=True;TrustServerCertificate=True;Encrypt=True";

        var options = new DbContextOptionsBuilder<SealedTraceDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new SealedTraceDbContext(options);
    }
}

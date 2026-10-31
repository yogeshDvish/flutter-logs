using Arovyx.SealedTrace.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Arovyx.SealedTrace.Tests;

[Collection(ApiTestGroup.Name)]
public sealed class MigrationTests(SealedTraceApiFactory factory)
{
    [Fact]
    public async Task Initial_migration_is_applied()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SealedTraceDbContext>();

        var pending = await db.Database.GetPendingMigrationsAsync();
        pending.Should().BeEmpty();

        var applied = await db.Database.GetAppliedMigrationsAsync();
        applied.Should().Contain(
            migration => migration.EndsWith("_InitialCreate", StringComparison.Ordinal));
    }
}

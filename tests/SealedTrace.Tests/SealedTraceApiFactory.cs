using Arovyx.SealedTrace.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Arovyx.SealedTrace.Tests;

/// <summary>
/// Boots the real API against a dedicated SQL Server Express database. Docker is
/// optional; set <c>SEALEDTRACE_TEST_CONNECTION</c> to point at another instance.
/// </summary>
public sealed class SealedTraceApiFactory : WebApplicationFactory<Program>
{
    public const string ConnectionStringVariable = "SEALEDTRACE_TEST_CONNECTION";

    private const string DefaultTestConnectionString =
        "Server=.\\SQLEXPRESS;Database=SealedTrace_Test;Trusted_Connection=True;TrustServerCertificate=True;Encrypt=True";

    public string ConnectionString { get; } =
        Environment.GetEnvironmentVariable(ConnectionStringVariable) ?? DefaultTestConnectionString;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = ConnectionString,
                ["Serilog:MinimumLevel:Default"] = "Warning",
            });
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SealedTraceDbContext>();
        db.Database.Migrate();

        return host;
    }
}

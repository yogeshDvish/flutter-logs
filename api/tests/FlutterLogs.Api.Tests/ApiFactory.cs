using FlutterLogs.Api.Infrastructure.Email;
using FlutterLogs.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace FlutterLogs.Api.Tests;

/// <summary>
/// Boots the real API against a dedicated test database. No Docker is required:
/// the database is created and migrated on first use of the SQL Server Express
/// instance named by <c>FLUTTERLOGS_TEST_CONNECTION</c>.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public const string ConnectionStringVariable = "FLUTTERLOGS_TEST_CONNECTION";

    // Test-only key. Never used outside the test host.
    private const string TestSigningKey =
        "test-only-signing-key-0123456789-0123456789-abcdef";

    // Test-only pepper. Never used outside the test host.
    private const string TestPepper =
        "test-only-pepper-0123456789-0123456789-abcdefgh";

    private const string DefaultTestConnectionString =
        "Server=.\\SQLEXPRESS;Database=FlutterLogs_Test;Trusted_Connection=True;"
        + "TrustServerCertificate=True;Encrypt=True";

    private readonly string _connectionString =
        Environment.GetEnvironmentVariable(ConnectionStringVariable)
        ?? DefaultTestConnectionString;

    public CapturingEmailSender Emails { get; } = new();

    public string ConnectionString => _connectionString;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Default", _connectionString);
        builder.UseSetting("Jwt:SigningKey", TestSigningKey);
        builder.UseSetting("Ingest:Pepper", TestPepper);
        builder.UseSetting("Auth:CookieSecure", "false");
        builder.UseSetting("Auth:DevEmailMode", "false");
        builder.UseSetting("RateLimiting:RegisterPerHour", "1000");
        builder.UseSetting("RateLimiting:LoginPerMinute", "1000");
        builder.UseSetting("RateLimiting:ForgotPasswordPerHour", "1000");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(Emails);
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);

        using var scope = host.Services.CreateScope();
        scope.ServiceProvider
            .GetRequiredService<AppDbContext>()
            .Database
            .Migrate();

        return host;
    }
}

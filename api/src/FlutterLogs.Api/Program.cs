using System.Globalization;
using System.Text;
using System.Threading.RateLimiting;
using FlutterLogs.Api.Common;
using FlutterLogs.Api.Features.Auth;
using FlutterLogs.Api.Features.Keys;
using FlutterLogs.Api.Features.Projects;
using FlutterLogs.Api.Infrastructure.Audit;
using FlutterLogs.Api.Infrastructure.Auth;
using FlutterLogs.Api.Infrastructure.Email;
using FlutterLogs.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Events;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "FlutterLogs.Api")
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning));

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection(AuthOptions.SectionName));
builder.Services.Configure<RateLimitOptions>(builder.Configuration.GetSection(RateLimitOptions.SectionName));
builder.Services.Configure<IngestOptions>(builder.Configuration.GetSection(IngestOptions.SectionName));
builder.Services.Configure<ProjectsOptions>(builder.Configuration.GetSection(ProjectsOptions.SectionName));

var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
    ?? new JwtOptions();
jwtOptions.Validate();

var ingestOptions = builder.Configuration.GetSection(IngestOptions.SectionName).Get<IngestOptions>()
    ?? new IngestOptions();
ingestOptions.Validate();

var projectOptions = builder.Configuration.GetSection(ProjectsOptions.SectionName).Get<ProjectsOptions>()
    ?? new ProjectsOptions();
projectOptions.Validate();

var rateLimits = builder.Configuration.GetSection(RateLimitOptions.SectionName).Get<RateLimitOptions>()
    ?? new RateLimitOptions();

var connectionString = builder.Configuration.GetConnectionString("Default");

if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "Connection string 'ConnectionStrings:Default' is not configured. " +
        "Set it in appsettings.Development.json, user-secrets, or ConnectionStrings__Default.");
}

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(
        connectionString,
        sql => sql.EnableRetryOnFailure(maxRetryCount: 3)));

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IPasswordHasher, Argon2PasswordHasher>();
builder.Services.AddSingleton<IJwtTokenService, JwtTokenService>();
builder.Services.AddSingleton<IEmailSender, DevConsoleEmailSender>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<AuditWriter>();
builder.Services.AddScoped<IngestionKeyIssuer>();
builder.Services.AddScoped<ProjectService>();
builder.Services.AddScoped<KeyService>();

builder.Services.AddSingleton<RegisterRequestValidator>();
builder.Services.AddSingleton<LoginRequestValidator>();
builder.Services.AddSingleton<VerifyEmailRequestValidator>();
builder.Services.AddSingleton<ForgotPasswordRequestValidator>();
builder.Services.AddSingleton<ResetPasswordRequestValidator>();
builder.Services.AddSingleton<CreateProjectRequestValidator>();
builder.Services.AddSingleton<UpdateProjectRequestValidator>();
builder.Services.AddSingleton<CreateProjectKeyRequestValidator>();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = "sub",
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy(
        "auth-register",
        context => RateLimitPartitions.FixedWindow(
            context,
            rateLimits.RegisterPerHour,
            TimeSpan.FromHours(1)));

    options.AddPolicy(
        "auth-login",
        context => RateLimitPartitions.FixedWindow(
            context,
            rateLimits.LoginPerMinute,
            TimeSpan.FromMinutes(1)));

    options.AddPolicy(
        "auth-forgot",
        context => RateLimitPartitions.FixedWindow(
            context,
            rateLimits.ForgotPasswordPerHour,
            TimeSpan.FromHours(1)));

    options.OnRejected = (context, _) =>
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter =
                ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        }

        return ValueTask.CompletedTask;
    };
});

var app = builder.Build();

// Order matters: correlation must exist before request logging and error handling,
// and the problem-details middleware must wrap the rate limiter to describe 429s.
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging();
app.UseExceptionHandler();
app.UseMiddleware<ProblemDetailsStatusCodeMiddleware>();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options => options.WithTitle("Flutter Logs API"));
}

app.MapGet("/api/v1/health", async (AppDbContext db, CancellationToken cancellationToken) =>
    {
        var databaseReachable = await db.Database.CanConnectAsync(cancellationToken);
        var payload = new HealthResponse(
            databaseReachable ? "Healthy" : "Unhealthy",
            databaseReachable ? "ok" : "unavailable",
            ApiInfo.Version);

        return databaseReachable
            ? Results.Ok(payload)
            : Results.Json(payload, statusCode: StatusCodes.Status503ServiceUnavailable);
    })
    .WithName("GetHealth")
    .WithSummary("Reports API liveness and database connectivity.")
    .AllowAnonymous();

app.MapAuthEndpoints();
app.MapProjectEndpoints();
app.MapKeyEndpoints();

app.Run();

/// <summary>Entry point marker used by integration tests.</summary>
public partial class Program;

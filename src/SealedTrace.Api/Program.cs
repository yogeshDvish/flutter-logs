using Arovyx.SealedTrace.Api.Common;
using Arovyx.SealedTrace.Api.Controllers;
using Arovyx.SealedTrace.Api.Endpoints;
using Arovyx.SealedTrace.Api.Security;
using Arovyx.SealedTrace.Api.Validation;
using Arovyx.SealedTrace.Core.Dtos.ApiKeys;
using Arovyx.SealedTrace.Core.Dtos.Ingest;
using Arovyx.SealedTrace.Core.Dtos.Logs;
using Arovyx.SealedTrace.Core.Dtos.Projects;
using Arovyx.SealedTrace.Core.Dtos.Stats;
using Arovyx.SealedTrace.Core.Interfaces;
using Arovyx.SealedTrace.Infrastructure.Auth;
using Arovyx.SealedTrace.Infrastructure.Caching;
using Arovyx.SealedTrace.Infrastructure.Crypto;
using Arovyx.SealedTrace.Infrastructure.Persistence;
using Arovyx.SealedTrace.Infrastructure.Services;
using Arovyx.SealedTrace.Jobs;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;
using Serilog.Events;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 2_097_152; // 2 MB
});

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "SealedTrace.Api")
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning));

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");

builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
    {
        context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
    };
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "SealedTrace API",
        Version = "v1",
        Description = "Encrypted, end-to-end sealed log ingestion and query API.",
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "JWT access token for console endpoints.",
    });

    options.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
    {
        Name = "X-Api-Key",
        Type = SecuritySchemeType.ApiKey,
        In = ParameterLocation.Header,
        Description = "Project ingestion key for /api/v1/ingest.",
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        [new OpenApiSecurityScheme
        {
            Reference = new OpenApiReference
            {
                Type = ReferenceType.SecurityScheme,
                Id = "Bearer",
            },
        }] = Array.Empty<string>(),
        [new OpenApiSecurityScheme
        {
            Reference = new OpenApiReference
            {
                Type = ReferenceType.SecurityScheme,
                Id = "ApiKey",
            },
        }] = Array.Empty<string>(),
    });
});

builder.Services.AddDbContext<SealedTraceDbContext>(options =>
    options.UseSqlServer(connectionString));

builder.Services.AddControllers();
builder.Services.AddMemoryCache();

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.Configure<MasterKeyOptions>(builder.Configuration.GetSection(MasterKeyOptions.SectionName));

builder.Services.AddScoped<IProjectService, ProjectService>();
builder.Services.AddScoped<IApiKeyService, ApiKeyService>();
builder.Services.AddScoped<IProjectKeyProtector, ProjectKeyProtector>();
builder.Services.AddScoped<MasterKeyRing>();
builder.Services.AddScoped<ProjectKeyCache>();
builder.Services.AddScoped<IValidator<CreateProjectRequest>, CreateProjectValidator>();
builder.Services.AddScoped<IValidator<UpdateProjectRequest>, UpdateProjectValidator>();
builder.Services.AddScoped<IIngestService, IngestService>();
builder.Services.AddScoped<IEnvelopeDecryptor, EnvelopeDecryptor>();
builder.Services.AddScoped<IValidator<IngestRequest>, IngestRequestValidator>();
builder.Services.AddScoped<SealedEnvelopeValidator>();
builder.Services.AddScoped<ILogQueryService, LogQueryService>();
builder.Services.AddScoped<IStatsService, StatsService>();
builder.Services.AddScoped<IValidator<LogQueryRequest>, LogQueryValidator>();
builder.Services.AddScoped<IValidator<StatsQueryRequest>, StatsQueryValidator>();

// Retention service and options.
builder.Services.Configure<Arovyx.SealedTrace.Infrastructure.Services.RetentionOptions>(
    builder.Configuration.GetSection(Arovyx.SealedTrace.Infrastructure.Services.RetentionOptions.SectionName));
builder.Services.AddScoped<RetentionService>();

// Rate limiting: fixed window for ingest and auth endpoints.
builder.Services.AddRateLimiter(rateLimiterOptions =>
{
    rateLimiterOptions.AddFixedWindowLimiter("ingest", options =>
    {
        options.PermitLimit = 600;
        options.Window = TimeSpan.FromMinutes(1);
        options.QueueLimit = 0;
    });

    rateLimiterOptions.AddFixedWindowLimiter("auth", options =>
    {
        options.PermitLimit = 30;
        options.Window = TimeSpan.FromMinutes(1);
        options.QueueLimit = 0;
    });
});

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? string.Empty,
            ValidAudience = builder.Configuration["Jwt:Audience"] ?? string.Empty,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(builder.Configuration["Jwt:SigningKey"] ?? string.Empty)),
            ClockSkew = TimeSpan.Zero,
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<JwtTokenService>();
builder.Services.AddScoped<RefreshTokenService>();
builder.Services.AddScoped<AuthRegisterValidator>();
builder.Services.AddScoped<AuthLoginValidator>();

builder.Services.AddHealthChecks()
    .AddDbContextCheck<SealedTraceDbContext>("database");

// Retention background worker (hosted service).
builder.Services.AddHostedService<RetentionBackgroundService>();

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? ["http://localhost:5174"];

builder.Services.AddCors(options =>
{
    options.AddPolicy("console", policy => policy
        .WithOrigins(allowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials());
});

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging();
app.UseExceptionHandler();
app.UseMiddleware<ProblemDetailsStatusCodeMiddleware>();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseCors("console");

app.UseAuthentication();
app.UseAuthorization();

app.UseRateLimiter();

// Security headers.
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    if (!app.Environment.IsDevelopment())
    {
        context.Response.Headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
    }
    await next();
});

// API key authentication for ingestion endpoints.
app.UseMiddleware<ApiKeyAuthenticationHandler>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "SealedTrace API v1");
        options.RoutePrefix = "swagger";
    });
}

app.MapControllers();

app.MapGet("/", () => Results.Ok(new
{
    name = "SealedTrace API",
    version = ApiInfo.Version,
    docs = app.Environment.IsDevelopment() ? "/swagger" : null,
})).WithTags("Meta").AllowAnonymous();

app.MapHealthEndpoints();

app.Run();

public partial class Program;

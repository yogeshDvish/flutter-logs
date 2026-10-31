# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy solution and restore dependencies
COPY SealedTrace.sln .
COPY Directory.Build.props .
COPY global.json .
COPY src/SealedTrace.Core/SealedTrace.Core.csproj src/SealedTrace.Core/
COPY src/SealedTrace.Infrastructure/SealedTrace.Infrastructure.csproj src/SealedTrace.Infrastructure/
COPY src/SealedTrace.Jobs/SealedTrace.Jobs.csproj src/SealedTrace.Jobs/
COPY src/SealedTrace.Api/SealedTrace.Api.csproj src/SealedTrace.Api/
COPY tests/SealedTrace.Tests/SealedTrace.Tests.csproj tests/SealedTrace.Tests/
RUN dotnet restore SealedTrace.sln

# Copy source code and publish
COPY src/ src/
COPY tests/ tests/
RUN dotnet publish src/SealedTrace.Api/SealedTrace.Api.csproj -c Release -o /app/publish --no-restore

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Install curl for healthchecks
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

# Non-root user
RUN adduser --disabled-password --gecos "" sealedtrace
USER sealedtrace

EXPOSE 5080 5081

ENV ASPNETCORE_URLS=http://+:5080
ENV ASPNETCORE_ENVIRONMENT=Production

HEALTHCHECK --interval=30s --timeout=10s --start-period=30s --retries=3 \
    CMD curl -f http://localhost:5080/health/ready || exit 1

ENTRYPOINT ["dotnet", "SealedTrace.Api.dll"]

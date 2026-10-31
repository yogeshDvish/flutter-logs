namespace FlutterLogs.Api.Tests;

/// <summary>
/// Shares one API host (and therefore one migration run) across all test classes.
/// Creating several hosts in parallel would race on the test database migration.
/// </summary>
[CollectionDefinition(Name)]
public sealed class ApiTestGroup : ICollectionFixture<ApiFactory>
{
    public const string Name = "api";
}

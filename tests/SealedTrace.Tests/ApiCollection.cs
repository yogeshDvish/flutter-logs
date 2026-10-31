namespace Arovyx.SealedTrace.Tests;

[CollectionDefinition(Name)]
public sealed class ApiTestGroup : ICollectionFixture<SealedTraceApiFactory>
{
    public const string Name = "sealedtrace-api";
}

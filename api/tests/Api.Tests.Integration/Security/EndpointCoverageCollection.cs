using Tests.Common;
using Xunit;

namespace Api.Tests.Integration.Security;

/// <summary>
/// One <see cref="IntegrationTestWebFactory"/> shared by the two guard classes that walk the application's real
/// endpoint table (<see cref="RateLimitCoverageTests"/> and <see cref="AuthorizationCoverageTests"/>).
///
/// The factory is normally an <c>IClassFixture</c>, which means one PostgreSQL database (migrated and seeded from
/// empty) and one full app boot per test class — an added class has a real time and memory cost. These two classes
/// issue no requests and touch no data: between them they need one object,
/// <see cref="Microsoft.AspNetCore.Routing.EndpointDataSource"/>. Sharing one fixture makes the second of them free
/// rather than doubling the price of asking the same question twice.
///
/// Classes in a collection run sequentially rather than in parallel. That costs nothing here — both classes are
/// in-memory assertions over an already-built route table — and it is the reason this collection is kept to the
/// two of them rather than becoming a general-purpose shared host.
/// </summary>
[CollectionDefinition(Name)]
public class EndpointCoverageCollection : ICollectionFixture<IntegrationTestWebFactory>
{
    public const string Name = "EndpointCoverage";
}
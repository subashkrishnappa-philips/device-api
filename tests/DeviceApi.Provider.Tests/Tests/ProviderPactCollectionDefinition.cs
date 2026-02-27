using Xunit;

namespace DeviceApi.Provider.Tests.Tests;

/// <summary>
/// Declares the "Provider Pact Verification" xUnit collection.
///
/// All test classes decorated with <c>[Collection("Provider Pact Verification")]</c>
/// share a single <see cref="Fixtures.DeviceApiProviderFixture"/> instance, so
/// the real Kestrel server is started exactly once for the entire provider test
/// run — efficient and predictable.
/// </summary>
[CollectionDefinition("Provider Pact Verification")]
public sealed class ProviderPactCollectionDefinition
    : ICollectionFixture<Fixtures.DeviceApiProviderFixture>
{
    // Intentionally empty — exists only to hold the CollectionDefinition attribute.
}

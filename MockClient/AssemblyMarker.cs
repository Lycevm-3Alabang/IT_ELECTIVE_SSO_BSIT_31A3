namespace MockClient;

/// <summary>
/// Public type that integration tests point WebApplicationFactory at.
/// The generated Program class of a top-level-statements app is internal and
/// the Gateway has its own, so the tests use a marker from each assembly instead.
/// </summary>
public sealed class AssemblyMarker
{
}

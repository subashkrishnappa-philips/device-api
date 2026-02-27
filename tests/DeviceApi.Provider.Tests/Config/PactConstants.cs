namespace DeviceApi.Provider.Tests.Config;

/// <summary>
/// Single source of truth for all Pact-related constants in the provider
/// project. Keep consumer-facing names in sync with
/// <c>DeviceApi.Consumer.Tests.Config.PactConstants</c>.
/// </summary>
public static class PactConstants
{
    // ── Participant names ────────────────────────────────────────────────────

    public const string ConsumerName = "DeviceApi-Consumer";
    public const string ProviderName = "DeviceApi";

    // ── File system paths ────────────────────────────────────────────────────

    /// <summary>
    /// Absolute path to the shared /contracts directory at the workspace root.
    ///
    /// Depth from AppContext.BaseDirectory (bin/Debug/net10.0):
    ///   net10.0 → Debug → bin → DeviceApi.Provider.Tests → tests → workspace root
    ///   = 5 levels up → then "contracts"
    /// </summary>
    public static readonly string PactDir = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "contracts"));

    /// <summary>Full path to the pact file written by the consumer project.</summary>
    public static string PactFilePath =>
        Path.Combine(PactDir, $"{ConsumerName}-{ProviderName}.json");

    /// <summary>
    /// Absolute path to the consumer registry JSON
    /// (<c>contracts/consumers.json</c>).
    /// </summary>
    public static string ConsumersRegistryPath =>
        Path.Combine(PactDir, "consumers.json");

    // ── Provider-state endpoint ──────────────────────────────────────────────

    /// <summary>
    /// Route the ProviderStateMiddleware listens on.
    /// Must match the value passed to <c>WithProviderStateUrl</c> in the verifier.
    /// </summary>
    public const string ProviderStatesPath = "/provider-states";
}

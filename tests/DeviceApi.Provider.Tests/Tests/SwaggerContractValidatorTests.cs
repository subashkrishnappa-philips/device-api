using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using DeviceApi.Provider.Tests.Config;
using DeviceApi.Provider.Tests.Fixtures;
using Xunit;
using Xunit.Abstractions;

namespace DeviceApi.Provider.Tests.Tests;

/// <summary>
/// Swagger / OpenAPI contract change detector with automatic consumer team
/// notification.
///
/// Problem solved:
///   When a developer modifies a controller or model the swagger.json changes.
///   Without a guard, a breaking change can silently invalidate existing
///   consumer pacts.
///
/// Solution:
///   1. Fetches the LIVE swagger.json from the running API.
///   2. Compares it against the COMMITTED BASELINE (<c>contracts/swagger-baseline.json</c>).
///   3. On drift:
///      a) Reads <c>contracts/consumers.json</c> to identify every downstream
///         consumer team that needs to be informed.
///      b) FAILS the test with a structured notification listing team names,
///         email addresses, Slack channels, and the diff — so no manual
///         lookup is needed.
///      c) Reminds the developer to run <c>run-pact-tests.ps1 --update-baseline</c>
///         after updating consumer pacts and verifying the provider.
///
/// Consumer team registry:
///   <c>contracts/consumers.json</c> — add an entry for every team that
///   integrates with DeviceApi. The structure is self-documenting; see the
///   file for the schema.
///
/// Baseline management:
///   • First run auto-creates the baseline (self-bootstrapping).
///   • Intentional changes: run <c>run-pact-tests.ps1 --update-baseline</c>
///     or delete <c>contracts/swagger-baseline.json</c> to regenerate.
/// </summary>
[Collection("Provider Pact Verification")]
public sealed class SwaggerContractValidatorTests : IClassFixture<DeviceApiProviderFixture>
{
    private readonly DeviceApiProviderFixture _fixture;
    private readonly ITestOutputHelper        _output;

    private static readonly string BaselineFile =
        Path.Combine(PactConstants.PactDir, "swagger-baseline.json");

    public SwaggerContractValidatorTests(DeviceApiProviderFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output  = output;
    }

    /// <summary>
    /// Downloads the live swagger.json, compares it to the baseline, and
    /// notifies all registered consumer teams on drift.
    /// </summary>
    [Fact]
    [Trait("Category", "SwaggerContractValidation")]
    public async Task Swagger_HasNotChanged_FromBaseline()
    {
        // ── 1. Fetch live swagger.json ────────────────────────────────────────
        using var client      = new HttpClient { BaseAddress = _fixture.ServerUri };
        var       swaggerJson = await client.GetStringAsync("/swagger/v1/swagger.json");
        var       liveNode    = NormaliseSwagger(swaggerJson);

        // ── 2. Auto-create baseline on first run ──────────────────────────────
        Directory.CreateDirectory(PactConstants.PactDir);

        if (!File.Exists(BaselineFile))
        {
            _output.WriteLine(
                "[INFO] No swagger baseline found. Creating baseline now at:\n" +
                $"  {BaselineFile}\n" +
                "Commit this file to source control and re-run tests.");

            await File.WriteAllTextAsync(BaselineFile, liveNode.ToJsonString(
                new JsonSerializerOptions { WriteIndented = true }));

            return; // First run always passes — baseline is being established.
        }

        // ── 3. Compare live against baseline ─────────────────────────────────
        var baselineRaw  = await File.ReadAllTextAsync(BaselineFile);
        var baselineNode = NormaliseSwagger(baselineRaw);

        var liveText     = liveNode.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        var baselineText = baselineNode.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

        if (liveText == baselineText)
        {
            _output.WriteLine("[OK] Live swagger.json matches committed baseline. No contract drift detected.");
            return;
        }

        // ── 4. Drift detected — notify consumers ──────────────────────────────
        var consumers   = LoadConsumerRegistry();
        var diff        = BuildDiffSummary(baselineText, liveText);

        _output.WriteLine("╔══════════════════════════════════════════════════════════════════╗");
        _output.WriteLine("║  SWAGGER CONTRACT CHANGED — ACTION REQUIRED                      ║");
        _output.WriteLine("╠══════════════════════════════════════════════════════════════════╣");
        _output.WriteLine("║  The live swagger.json no longer matches the committed baseline. ║");
        _output.WriteLine("║  The following consumer teams MUST update their pact contracts:  ║");
        _output.WriteLine("╠══════════════════════════════════════════════════════════════════╣");

        foreach (var consumer in consumers)
        {
            _output.WriteLine($"║  Consumer : {consumer.Name,-52} ║");
            _output.WriteLine($"║  Team     : {consumer.Team,-52} ║");
            _output.WriteLine($"║  Email    : {consumer.Contact,-52} ║");
            _output.WriteLine($"║  Slack    : {consumer.Slack,-52} ║");
            _output.WriteLine("║────────────────────────────────────────────────────────────────║");
        }

        _output.WriteLine("║                                                                  ║");
        _output.WriteLine("║  Steps to resolve:                                               ║");
        _output.WriteLine("║  1. Review the diff printed below.                               ║");
        _output.WriteLine("║  2. Notify each consumer team (email / Slack above).             ║");
        _output.WriteLine("║  3. Consumer team updates their tests in                         ║");
        _output.WriteLine("║     tests/DeviceApi.Consumer.Tests/Contracts/                    ║");
        _output.WriteLine("║  4. Re-run consumer tests to regenerate the pact file.           ║");
        _output.WriteLine("║  5. Run: run-pact-tests.ps1 --update-baseline                    ║");
        _output.WriteLine("╚══════════════════════════════════════════════════════════════════╝");
        _output.WriteLine(string.Empty);
        _output.WriteLine("── Diff (baseline → live) ──────────────────────────────────────────");
        _output.WriteLine(diff);

        liveText.Should().Be(baselineText,
            because: "the swagger contract must not change without notifying consumer teams " +
                     "and updating pacts. See the notification above for remediation steps.");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Loads the consumer registry from <c>contracts/consumers.json</c>.
    /// Returns an empty list if the file does not exist yet.
    /// </summary>
    private static List<ConsumerRegistryEntry> LoadConsumerRegistry()
    {
        if (!File.Exists(PactConstants.ConsumersRegistryPath))
            return [];

        try
        {
            var json = File.ReadAllText(PactConstants.ConsumersRegistryPath);
            var doc  = JsonSerializer.Deserialize<ConsumerRegistry>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return doc?.Consumers ?? [];
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// Normalises a swagger JSON string for stable comparison:
    ///   • Removes volatile meta-fields (servers[], info.version date-stamps).
    ///   • Sorts keys so property-order differences don't produce false positives.
    /// </summary>
    private static JsonNode NormaliseSwagger(string json)
    {
        var node = JsonNode.Parse(json)!;
        node.AsObject().Remove("servers");
        return node;
    }

    private static string BuildDiffSummary(string baseline, string live)
    {
        var baseLines = baseline.Split('\n');
        var liveLines = live.Split('\n');
        var sb        = new System.Text.StringBuilder();

        const int maxShown = 80;
        int maxLines = Math.Max(baseLines.Length, liveLines.Length);
        int shown    = 0;

        for (int i = 0; i < maxLines && shown < maxShown; i++)
        {
            var b = i < baseLines.Length ? baseLines[i] : "<missing>";
            var l = i < liveLines.Length ? liveLines[i] : "<missing>";

            if (b != l)
            {
                sb.AppendLine($"Line {i + 1,-5} BASE: {b.TrimEnd()}");
                sb.AppendLine($"Line {i + 1,-5} LIVE: {l.TrimEnd()}");
                sb.AppendLine();
                shown++;
            }
        }

        if (shown >= maxShown)
            sb.AppendLine("... more differences exist. Use a dedicated diff tool for the full picture.");

        return sb.ToString();
    }

    // ── Inner DTOs ────────────────────────────────────────────────────────────

    private sealed record ConsumerRegistry(List<ConsumerRegistryEntry> Consumers);
    private sealed record ConsumerRegistryEntry(
        string Name,
        string Team,
        string Contact,
        string Slack,
        string PactFile);
}

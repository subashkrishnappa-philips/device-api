using System.Text.Json;
using DeviceApi.Provider.Tests.Config;

namespace DeviceApi.Provider.Tests.Middleware;

/// <summary>
/// ASP.NET Core middleware that intercepts <c>POST /provider-states</c>
/// requests emitted by the PactNet verifier before each interaction replay.
///
/// The verifier POSTs a body like:
/// <code>
/// { "action": "setup", "state": "&lt;state name&gt;", "params": { ... } }
/// </code>
///
/// For stateless APIs (DeviceApi currently has no state) no data seeding is
/// required; the middleware returns 200 OK for every known state name.
///
/// As the API evolves, add provider-state handlers to the
/// <see cref="ProviderStates"/> dictionary to seed test databases, configure
/// mocks, etc.
/// </summary>
public sealed class ProviderStateMiddleware : IMiddleware
{
    private readonly ILogger<ProviderStateMiddleware> _logger;

    /// <summary>
    /// Maps provider-state names → setup delegates.
    ///
    /// Extend this as new consumer states are introduced.
    /// State names must match the strings used in consumer tests
    /// (<c>pact.Given("...")</c>).
    /// </summary>
    private static readonly IReadOnlyDictionary<string, Action> ProviderStates =
        new Dictionary<string, Action>(StringComparer.OrdinalIgnoreCase)
        {
            // Example — uncomment and extend as your API gains state:
            // ["a device exists with serial SN-001"] = () => DeviceSeeder.Seed("SN-001"),
        };

    public ProviderStateMiddleware(ILogger<ProviderStateMiddleware> logger)
    {
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        if (context.Request.Method == HttpMethods.Post
            && context.Request.Path.StartsWithSegments(PactConstants.ProviderStatesPath))
        {
            await HandleProviderStateAsync(context);
            return;
        }

        await next(context);
    }

    private async Task HandleProviderStateAsync(HttpContext context)
    {
        using var reader   = new StreamReader(context.Request.Body);
        var       bodyJson = await reader.ReadToEndAsync();

        ProviderStateRequest? request = null;

        if (!string.IsNullOrWhiteSpace(bodyJson))
        {
            request = JsonSerializer.Deserialize<ProviderStateRequest>(
                bodyJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }

        var stateName = request?.State ?? string.Empty;

        if (!string.IsNullOrEmpty(stateName))
        {
            if (ProviderStates.TryGetValue(stateName, out var setup))
            {
                _logger.LogInformation("Provider state setup: {State}", stateName);
                setup();
            }
            else
            {
                // Unknown but non-fatal for stateless scenarios.
                _logger.LogWarning(
                    "No handler for provider state: '{State}'. " +
                    "Add an entry to ProviderStateMiddleware.ProviderStates if needed.",
                    stateName);
            }
        }

        context.Response.StatusCode = StatusCodes.Status200OK;
        await context.Response.WriteAsync("OK");
    }

    // ── Inner DTOs ────────────────────────────────────────────────────────────

    private sealed record ProviderStateRequest(string? State, string? Action);
}

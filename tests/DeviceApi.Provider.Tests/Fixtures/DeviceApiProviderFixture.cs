using System.Net;
using System.Net.Sockets;
using DeviceApi.Provider.Tests.Middleware;
using Xunit;

namespace DeviceApi.Provider.Tests.Fixtures;

/// <summary>
/// xUnit class-fixture that boots the real DeviceApi on a deterministic free
/// TCP port so PactNet can connect over a real TCP socket.
///
/// Design decisions:
///   1. Calls <see cref="ApiBootstrap.CreateWebApplication"/> directly from the
///      <c>src/DeviceApi</c> project. No code is duplicated in the test project.
///
///   2. Uses the <c>configureBuilder</c> callback to bind Kestrel to a free
///      port and register <see cref="ProviderStateMiddleware"/> in DI.
///
///   3. Uses the <c>configureEarlyMiddleware</c> callback to prepend
///      <see cref="ProviderStateMiddleware"/> BEFORE Swagger, routing, and
///      controllers so the verifier's provider-state requests are intercepted
///      before reaching the normal pipeline.
///
///   4. Implements <see cref="IAsyncLifetime"/> so xUnit calls
///      <see cref="InitializeAsync"/> before any test runs.
/// </summary>
public sealed class DeviceApiProviderFixture : IAsyncLifetime
{
    /// <summary>The base URI the real Kestrel server is listening on.</summary>
    public Uri ServerUri { get; }

    private WebApplication? _app;

    public DeviceApiProviderFixture()
    {
        ServerUri = new Uri($"http://localhost:{FreeTcpPort()}");
    }

    // ── IAsyncLifetime ────────────────────────────────────────────────────────

    public async Task InitializeAsync()
    {
        _app = ApiBootstrap.CreateWebApplication(
            configureBuilder: b =>
            {
                // Bind Kestrel to our deterministic free port.
                b.WebHost.UseUrls(ServerUri.ToString());

                // ProviderStateMiddleware implements IMiddleware, so it must
                // be registered in DI before UseMiddleware<> can resolve it.
                b.Services.AddTransient<ProviderStateMiddleware>();
            },
            configureEarlyMiddleware: app =>
            {
                // Prepend provider-state handling before normal pipeline.
                app.UseMiddleware<ProviderStateMiddleware>();
            });

        await _app.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static int FreeTcpPort()
    {
        using var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }
}

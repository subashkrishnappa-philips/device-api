using System.Reflection;
using Microsoft.OpenApi;

// ─── Entry point (skipped when running inside a test host) ───────────────────
if (args.FirstOrDefault() != "--no-run")
{
    var app = ApiBootstrap.CreateWebApplication(args);
    app.Run();
}

/// <summary>
/// Static factory that constructs and configures a <see cref="WebApplication"/>
/// for DeviceApi.
///
/// Exposed as a public, non-sealed class so provider test fixtures can call
/// <see cref="CreateWebApplication"/> directly — bypassing
/// <c>WebApplicationFactory</c> which no longer supports Kestrel overrides in
/// .NET 10 minimal-hosting model.
///
/// Design principle: the factory knows nothing about test-specific middleware
/// (e.g. <c>ProviderStateMiddleware</c>). Test concerns are injected via the
/// optional callbacks, keeping production code free of test artefacts.
/// </summary>
public static class ApiBootstrap
{
    /// <summary>
    /// Builds and configures the <see cref="WebApplication"/>.
    /// </summary>
    /// <param name="args">Command-line arguments (unused in production; passed for completeness).</param>
    /// <param name="configureBuilder">
    ///   Optional. Called after all default services are registered.
    ///   Use to bind Kestrel to a specific port, register test-only services, etc.
    /// </param>
    /// <param name="configureEarlyMiddleware">
    ///   Optional. Called immediately after <see cref="WebApplication.Build"/> and
    ///   BEFORE the standard middleware pipeline.  Use to prepend test-only middleware
    ///   (e.g. provider-state handling) at the very front of the pipeline.
    /// </param>
    public static WebApplication CreateWebApplication(
        string[]?                      args                     = null,
        Action<WebApplicationBuilder>? configureBuilder         = null,
        Action<WebApplication>?        configureEarlyMiddleware = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args            = args ?? [],
            // Explicit application name ensures controller discovery resolves
            // to this assembly even when called from a test host where
            // Assembly.GetEntryAssembly() returns testhost.dll.
            ApplicationName = typeof(ApiBootstrap).Assembly.GetName().Name
        });

        // ─── MVC / Controllers ────────────────────────────────────────────────
        builder.Services.AddControllers();

        // ─── Swagger / OpenAPI ────────────────────────────────────────────────
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title       = "Device Management API",
                Version     = "v1",
                Description = "REST API for managing device information. " +
                              "Provides endpoints to create and update device-to-user associations.",
                Contact = new OpenApiContact
                {
                    Name  = "Platform API Team",
                    Email = "api-support@example.com"
                },
                License = new OpenApiLicense
                {
                    Name = "MIT",
                    Url  = new Uri("https://opensource.org/licenses/MIT")
                }
            });

            var xmlFile = $"{typeof(ApiBootstrap).Assembly.GetName().Name}.xml";
            var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
            if (File.Exists(xmlPath))
                options.IncludeXmlComments(xmlPath);
        });

        // ─── Test-fixture hook: extra services / URL binding ──────────────────
        configureBuilder?.Invoke(builder);

        var app = builder.Build();

        // ─── Test-fixture hook: prepend test-only middleware ──────────────────
        configureEarlyMiddleware?.Invoke(app);

        // ─── Middleware Pipeline ──────────────────────────────────────────────
        app.UseSwagger(c =>
        {
            c.RouteTemplate = "swagger/{documentName}/swagger.json";
        });

        app.UseSwaggerUI(c =>
        {
            c.SwaggerEndpoint("/swagger/v1/swagger.json", "Device Management API v1");
            c.RoutePrefix        = "swagger";
            c.DocumentTitle      = "Device Management API";
            c.DefaultModelsExpandDepth(-1);
        });

        app.UseHttpsRedirection();
        app.UseAuthorization();
        app.MapControllers();

        return app;
    }
}

using System.Text.Json;
using SwaggerPactGenerator;

// ─────────────────────────────────────────────────────────────────────────────
// SwaggerPactGenerator
// ─────────────────────────────────────────────────────────────────────────────
// Reads the DeviceApi swagger.json, compares it against the existing pact file,
// and generates skeleton consumer test stubs for every uncovered endpoint.
//
// Usage:
//   SwaggerPactGenerator --swagger-file <path>      (from a local file)
//   SwaggerPactGenerator --swagger-url  <url>       (from a running API)
//   SwaggerPactGenerator --pact-file    <path>      (existing pact, for diffing)
//   SwaggerPactGenerator --consumer-output <path>   (where to write stubs)
//   SwaggerPactGenerator --notification  <path>     (optional Markdown report)
//
// Called automatically by run-pact-tests.ps1 before running the test suite.
// ─────────────────────────────────────────────────────────────────────────────

var cliArgs    = Args.Parse(System.Environment.GetCommandLineArgs()[1..]);
var parser     = new SwaggerParser();
var generator  = new ConsumerTestGenerator();
var notifier   = new ConsumerNotificationReport();

Console.ForegroundColor = ConsoleColor.Cyan;
Console.WriteLine("╔══════════════════════════════════════════════════════╗");
Console.WriteLine("║   SwaggerPactGenerator — Contract Stub Generator     ║");
Console.WriteLine("╚══════════════════════════════════════════════════════╝");
Console.ResetColor();

// ── 1. Load swagger ─────────────────────────────────────────────────────────
string swaggerJson;

if (!string.IsNullOrEmpty(cliArgs.SwaggerFile))
{
    if (!File.Exists(cliArgs.SwaggerFile))
    {
        Console.Error.WriteLine($"[ERROR] Swagger file not found: {cliArgs.SwaggerFile}");
        return 1;
    }
    swaggerJson = await File.ReadAllTextAsync(cliArgs.SwaggerFile);
    Console.WriteLine($"[INFO] Loaded swagger from file: {cliArgs.SwaggerFile}");
}
else if (!string.IsNullOrEmpty(cliArgs.SwaggerUrl))
{
    using var client = new HttpClient();
    swaggerJson      = await client.GetStringAsync(cliArgs.SwaggerUrl);
    Console.WriteLine($"[INFO] Fetched swagger from URL: {cliArgs.SwaggerUrl}");
}
else
{
    Console.Error.WriteLine("[ERROR] Provide --swagger-file or --swagger-url.");
    PrintUsage();
    return 1;
}

var operations = parser.ExtractOperations(swaggerJson);
Console.WriteLine($"[INFO] Found {operations.Count} operation(s) in swagger.");

// ── 2. Find uncovered operations ─────────────────────────────────────────────
List<ApiOperation> uncovered;

if (!string.IsNullOrEmpty(cliArgs.PactFile) && File.Exists(cliArgs.PactFile))
{
    var pactJson  = await File.ReadAllTextAsync(cliArgs.PactFile);
    var covered   = parser.ExtractPactInteractions(pactJson);
    uncovered     = operations
        .Where(op => !covered.Any(c =>
            string.Equals(c.Method, op.Method, StringComparison.OrdinalIgnoreCase)
            && string.Equals(c.Path, op.Path, StringComparison.OrdinalIgnoreCase)))
        .ToList();

    Console.WriteLine($"[INFO] {covered.Count} operation(s) already covered by pact interactions.");
    Console.WriteLine($"[INFO] {uncovered.Count} operation(s) require new stubs.");
}
else
{
    uncovered = operations;
    Console.WriteLine("[INFO] No pact file provided — generating stubs for all operations.");
}

if (uncovered.Count == 0)
{
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine("[OK]  All swagger operations are covered. No stubs generated.");
    Console.ResetColor();
    return 0;
}

// ── 3. Generate consumer test stubs ─────────────────────────────────────────
var outputDir = cliArgs.ConsumerOutput ??
    Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "..", "..",
        "tests", "DeviceApi.Consumer.Tests", "Generated"));

Directory.CreateDirectory(outputDir);

int generated = 0;

foreach (var op in uncovered)
{
    var filePath = generator.GenerateStub(op, outputDir);
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine($"[GEN] Created stub: {filePath}");
    Console.ResetColor();
    generated++;
}

Console.WriteLine($"[INFO] Generated {generated} stub(s) in: {outputDir}");

// ── 4. Write consumer notification report ────────────────────────────────────
if (!string.IsNullOrEmpty(cliArgs.NotificationFile))
{
    var consumersFile = Path.Combine(
        Path.GetDirectoryName(cliArgs.NotificationFile)!,
        "consumers.json");

    await notifier.WriteReportAsync(
        cliArgs.NotificationFile,
        uncovered,
        consumersRegistryPath: consumersFile);

    Console.WriteLine($"[INFO] Consumer notification report: {cliArgs.NotificationFile}");
}

Console.ForegroundColor = ConsoleColor.Green;
Console.WriteLine($"[DONE] {generated} stub(s) created. Consumer teams must review Generated/ folder.");
Console.ResetColor();

return 0;

void PrintUsage()
{
    Console.WriteLine("""
    Usage:
      SwaggerPactGenerator [options]

    Options:
      --swagger-file <path>       Path to a local swagger.json file
      --swagger-url  <url>        URL to fetch swagger.json from (e.g. http://localhost:5000/swagger/v1/swagger.json)
      --pact-file    <path>       Path to existing pact file (optional, for diffing)
      --consumer-output <path>    Output directory for generated consumer test stubs
      --notification <path>       Path for the Markdown consumer notification report

    Examples:
      SwaggerPactGenerator --swagger-url http://localhost:5000/swagger/v1/swagger.json
      SwaggerPactGenerator --swagger-file contracts/swagger-baseline.json --pact-file contracts/DeviceApi-Consumer-DeviceApi.json
    """);
}

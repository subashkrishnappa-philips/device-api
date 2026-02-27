namespace SwaggerPactGenerator;

/// <summary>
/// Parses command-line arguments for the generator.
/// </summary>
public sealed class Args
{
    public string? SwaggerFile     { get; private set; }
    public string? SwaggerUrl      { get; private set; }
    public string? PactFile        { get; private set; }
    public string? ConsumerOutput  { get; private set; }
    public string? NotificationFile { get; private set; }

    public static Args Parse(string[] args)
    {
        var result = new Args();

        for (int i = 0; i < args.Length - 1; i++)
        {
            switch (args[i])
            {
                case "--swagger-file":     result.SwaggerFile      = args[++i]; break;
                case "--swagger-url":      result.SwaggerUrl       = args[++i]; break;
                case "--pact-file":        result.PactFile         = args[++i]; break;
                case "--consumer-output":  result.ConsumerOutput   = args[++i]; break;
                case "--notification":     result.NotificationFile = args[++i]; break;
            }
        }

        return result;
    }
}

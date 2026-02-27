using System.Text.Json.Nodes;

namespace SwaggerPactGenerator;

/// <summary>
/// Represents a single API operation extracted from swagger or a pact file.
/// </summary>
/// <param name="Method">HTTP method (get, post, put, etc.).</param>
/// <param name="Path">URL path (e.g. /api/UpdateDeviceInformation).</param>
/// <param name="OperationId">Optional operationId from swagger.</param>
/// <param name="Summary">Optional summary from swagger.</param>
/// <param name="RequestBodySchema">Optional JSON schema of the request body.</param>
/// <param name="Responses">Status code → description map.</param>
public sealed record ApiOperation(
    string Method,
    string Path,
    string? OperationId         = null,
    string? Summary             = null,
    JsonNode? RequestBodySchema = null,
    Dictionary<string, string>? Responses = null);

/// <summary>
/// Extracts API operations from swagger JSON and pact interaction files.
/// </summary>
public sealed class SwaggerParser
{
    /// <summary>
    /// Parses a swagger JSON document and returns all HTTP operations found
    /// under the <c>paths</c> object.
    /// </summary>
    public List<ApiOperation> ExtractOperations(string swaggerJson)
    {
        var node  = JsonNode.Parse(swaggerJson)!;
        var paths = node["paths"]?.AsObject();
        var ops   = new List<ApiOperation>();

        if (paths is null)
            return ops;

        foreach (var (path, methods) in paths)
        {
            foreach (var (method, opNode) in methods!.AsObject())
            {
                if (!IsHttpVerb(method))
                    continue;

                var operationId = opNode?["operationId"]?.GetValue<string>();
                var summary     = opNode?["summary"]?.GetValue<string>();

                // Extract request body schema (first content type)
                JsonNode? bodySchema = null;
                var requestBody = opNode?["requestBody"]?["content"];
                if (requestBody is JsonObject contentObj)
                {
                    foreach (var (_, mediaTypeNode) in contentObj)
                    {
                        bodySchema = mediaTypeNode?["schema"];
                        break;
                    }
                }

                // Extract response codes
                var responses = new Dictionary<string, string>();
                var responsesNode = opNode?["responses"]?.AsObject();
                if (responsesNode is not null)
                {
                    foreach (var (statusCode, responseNode) in responsesNode)
                    {
                        var desc = responseNode?["description"]?.GetValue<string>() ?? string.Empty;
                        responses[statusCode] = desc;
                    }
                }

                ops.Add(new ApiOperation(
                    Method: method,
                    Path: path,
                    OperationId: operationId,
                    Summary: summary,
                    RequestBodySchema: bodySchema,
                    Responses: responses));
            }
        }

        return ops;
    }

    /// <summary>
    /// Extracts the request path + method from every interaction in a pact JSON file.
    /// </summary>
    public List<ApiOperation> ExtractPactInteractions(string pactJson)
    {
        var node         = JsonNode.Parse(pactJson)!;
        var interactions = node["interactions"]?.AsArray();
        var ops          = new List<ApiOperation>();

        if (interactions is null)
            return ops;

        foreach (var interaction in interactions)
        {
            var request = interaction?["request"];
            var method  = request?["method"]?.GetValue<string>();
            var path    = request?["path"]?.GetValue<string>();

            if (method is not null && path is not null)
                ops.Add(new ApiOperation(method, path));
        }

        return ops;
    }

    private static bool IsHttpVerb(string method) =>
        method is "get" or "post" or "put" or "patch" or "delete" or "head" or "options";
}

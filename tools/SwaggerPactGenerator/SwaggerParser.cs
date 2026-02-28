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
/// <param name="RequestBodyFields">Top-level field names of the request body (resolved from $ref).</param>
/// <param name="Responses">Status code → description map.</param>
public sealed record ApiOperation(
    string Method,
    string Path,
    string? OperationId                   = null,
    string? Summary                       = null,
    JsonNode? RequestBodySchema           = null,
    IReadOnlySet<string>? RequestBodyFields = null,
    Dictionary<string, string>? Responses = null);

/// <summary>
/// Represents schema drift on an operation that IS covered by a pact but whose
/// request body has changed (fields added or removed).
/// </summary>
/// <param name="SwaggerOperation">The swagger operation with the new schema.</param>
/// <param name="AddedFields">Fields present in swagger but absent from every pact interaction.</param>
/// <param name="RemovedFields">Fields present in pact interactions but absent from swagger.</param>
public sealed record SchemaDriftOperation(
    ApiOperation          SwaggerOperation,
    IReadOnlyList<string> AddedFields,
    IReadOnlyList<string> RemovedFields)
{
    public bool HasDrift => AddedFields.Count > 0 || RemovedFields.Count > 0;
}

/// <summary>
/// Extracts API operations from swagger JSON and pact interaction files.
/// </summary>
public sealed class SwaggerParser
{
    /// <summary>
    /// Parses a swagger JSON document and returns all HTTP operations found
    /// under the <c>paths</c> object. Resolves <c>$ref</c> schemas so that
    /// <see cref="ApiOperation.RequestBodyFields"/> is populated.
    /// </summary>
    public List<ApiOperation> ExtractOperations(string swaggerJson)
    {
        var doc   = JsonNode.Parse(swaggerJson)!;
        var paths = doc["paths"]?.AsObject();
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

                // Extract request body schema (first content type) + resolve field names
                JsonNode? bodySchema = null;
                IReadOnlySet<string>? requestFields = null;

                var requestBody = opNode?["requestBody"]?["content"];
                if (requestBody is JsonObject contentObj)
                {
                    foreach (var (_, mediaTypeNode) in contentObj)
                    {
                        bodySchema = mediaTypeNode?["schema"];
                        break;
                    }
                }

                if (bodySchema is not null)
                    requestFields = ResolveFieldNames(bodySchema, doc);

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
                    RequestBodyFields: requestFields,
                    Responses: responses));
            }
        }

        return ops;
    }

    /// <summary>
    /// Extracts covered operations from a pact JSON file.
    /// For each unique path+method, unions the top-level body field names
    /// across all interactions so the drift check sees the full covered surface.
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

            if (method is null || path is null)
                continue;

            // Extract top-level field names from the pact request body.
            // PactNet V4 wraps the actual payload under body.content (PactV4 format);
            // older specs place the payload directly in body.
            IReadOnlySet<string>? bodyFields = null;
            var bodyNode = request?["body"];
            // PactNet V4: { "content": { ... }, "contentType": "...", "encoded": false }
            var actualBody = bodyNode?["content"] ?? bodyNode;
            if (actualBody is JsonObject bodyObj)
                bodyFields = new HashSet<string>(bodyObj.Select(kv => kv.Key), StringComparer.OrdinalIgnoreCase);

            var existing = ops.FirstOrDefault(o =>
                string.Equals(o.Method, method, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(o.Path,   path,   StringComparison.OrdinalIgnoreCase));

            if (existing is null)
            {
                // First interaction for this path+method
                ops.Add(new ApiOperation(method, path, RequestBodyFields: bodyFields));
            }
            else
            {
                // Union fields from additional interactions covering the same endpoint
                if (bodyFields is not null)
                {
                    var merged = new HashSet<string>(
                        existing.RequestBodyFields ?? Enumerable.Empty<string>(),
                        StringComparer.OrdinalIgnoreCase);
                    merged.UnionWith(bodyFields);
                    var idx = ops.IndexOf(existing);
                    ops[idx] = existing with { RequestBodyFields = merged };
                }
            }
        }

        return ops;
    }

    /// <summary>
    /// Compares swagger operations against covered pact operations and returns
    /// any operation whose request body schema has drifted (fields added or removed).
    /// Only operations that ARE already covered by a pact interaction are checked;
    /// entirely uncovered operations are handled by the normal stub-generation path.
    /// </summary>
    public List<SchemaDriftOperation> DetectSchemaDrift(
        List<ApiOperation> swaggerOps,
        List<ApiOperation> pactOps)
    {
        var results = new List<SchemaDriftOperation>();

        foreach (var swaggerOp in swaggerOps)
        {
            // Only check operations already covered by the pact
            var pactOp = pactOps.FirstOrDefault(p =>
                string.Equals(p.Method, swaggerOp.Method, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(p.Path,   swaggerOp.Path,   StringComparison.OrdinalIgnoreCase));

            if (pactOp is null)                     continue; // uncovered — handled separately
            if (swaggerOp.RequestBodyFields is null) continue; // no request body to compare

            var swaggerFields = swaggerOp.RequestBodyFields;
            var pactFields    = pactOp.RequestBodyFields ?? new HashSet<string>();

            var added   = swaggerFields.Except(pactFields,   StringComparer.OrdinalIgnoreCase).OrderBy(f => f).ToList();
            var removed = pactFields.Except(swaggerFields,   StringComparer.OrdinalIgnoreCase).OrderBy(f => f).ToList();

            if (added.Count > 0 || removed.Count > 0)
                results.Add(new SchemaDriftOperation(swaggerOp, added, removed));
        }

        return results;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>Resolves a schema node (following $ref) and returns its property names.</summary>
    private static IReadOnlySet<string>? ResolveFieldNames(JsonNode schemaNode, JsonNode doc)
    {
        var refStr = schemaNode["$ref"]?.GetValue<string>();
        var resolved = refStr is not null ? ResolveRef(refStr, doc) ?? schemaNode : schemaNode;

        var props = resolved["properties"]?.AsObject();
        if (props is null) return null;

        return new HashSet<string>(props.Select(kv => kv.Key), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Resolves a local JSON Reference like <c>#/components/schemas/Foo</c>.</summary>
    private static JsonNode? ResolveRef(string refStr, JsonNode doc)
    {
        if (!refStr.StartsWith("#/")) return null;
        JsonNode? node = doc;
        foreach (var segment in refStr[2..].Split('/'))
            node = node?[segment];
        return node;
    }

    private static bool IsHttpVerb(string method) =>
        method is "get" or "post" or "put" or "patch" or "delete" or "head" or "options";
}

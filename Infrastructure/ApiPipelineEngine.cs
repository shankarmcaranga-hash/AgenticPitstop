using System.Data;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace AgenticPitstop.Infrastructure;

// =============================================================================
// Configuration model (stored as JSON on the pipeline and its steps)
// =============================================================================

/// <summary>Sets one value in the outgoing request.</summary>
/// <summary>Sets one value in the outgoing request.</summary>
public sealed class PipelineBinding
{
    public string In { get; set; } = "body";      // body | query | header | path | form
    public string Target { get; set; }            // body: exact $.path; otherwise a name
    public string Kind { get; set; } = "text";    // form only: text | file | inline
    public string Source { get; set; } = "const"; // inflow | json | record | const | system
    public string Step { get; set; }              // json source: an earlier step key
    public string Value { get; set; }             // field, json path, constant or system name

    // "inline" only: the file is built from the resolved value, not read from disk.
    public string FileName { get; set; }          // the name the part is sent under
    public string ContentType { get; set; }       // defaults to application/json
}

public sealed class PipelineCondition
{
    public string Source { get; set; } = "inflow"; // inflow | json | record
    public string Step { get; set; }
    public string Path { get; set; }
    public string Op { get; set; } = "eq";         // eq ne contains startsWith endsWith empty notEmpty in gt lt
    public string Value { get; set; }
}

public sealed class PipelineDriver
{
    public string Match { get; set; } = "all";     // all | any
    public List<PipelineCondition> Conditions { get; set; } = new();
}

/// <summary>One folder level, built from a value rather than typed.</summary>
public sealed class PipelineFolderPart
{
    public string Source { get; set; } = "const";   // const | json | inflow | record | system
    public string Step { get; set; }
    public string Value { get; set; }               // a JSON path, a field name or literal text
}

public sealed class PipelineFileMap
{
    public string Step { get; set; }
    public string ContentStep { get; set; }
    public string NameStep { get; set; }
    public string ContentPath { get; set; }
    public string NamePath { get; set; }
    public string Folder { get; set; }              // the {Token} template, kept for existing pipelines
    public string Root { get; set; }                // alias from ApiConfigurator:FileRoots, empty = CollectionFileRoot
    public List<PipelineFolderPart> Parts { get; set; } = new();
}
public sealed class PipelineColumnMap
{
    public string Column { get; set; }
    public string Source { get; set; } = "json";   // json | inflow | record | const | system | file
    public string Step { get; set; }
    public string Path { get; set; }
    public string Value { get; set; }
    public PipelineFileMap File { get; set; }
}


/// <summary>Rows written after one step, into one collection.</summary>
public sealed class PipelineOutput
{
    public string Step { get; set; }
    public int CollectionId { get; set; }
    public List<PipelineColumnMap> Columns { get; set; } = new();
    public List<string> Dedup { get; set; } = new();

    // insert | upsert. Default keeps every saved output behaving as before.
    public string Mode { get; set; } = "insert";

    // Default true, so pipelines saved before these existed behave as before.
    public bool AutoInflowDataId { get; set; } = true;
    public bool AutoUseCaseId { get; set; } = true;
    public bool AutoCreatedBy { get; set; } = true;
}

public sealed class PipelineMapping
{
    // Columns and Dedup are the old single-output shape, still read for pipelines
    // saved before outputs existed.
    public List<PipelineColumnMap> Columns { get; set; } = new();
    public List<string> Dedup { get; set; } = new();
    public List<PipelineOutput> Outputs { get; set; } = new();
}

public sealed class PipelineStepConfig
{
    public string Key { get; set; }
    public string Kind { get; set; } = "API";       // API | ROWS
    public int SourceCollectionId { get; set; }     // ROWS: the collection its rows come from
    public int ConnectorId { get; set; }
    public int ConnectorVersionId { get; set; }
    public string BodyMode { get; set; } = "JSON";
    public List<PipelineBinding> Bindings { get; set; } = new();
    public string LoopPath { get; set; }
    public List<PipelineCondition> ItemConditions { get; set; } = new();
    public string SampleResponseJson { get; set; }
}

public sealed class PipelineRunSummary
{
    public int RunId { get; set; }
    public string Status { get; set; } = "Running";
    public int RecordsConsidered { get; set; }
    public int RecordsSucceeded { get; set; }
    public int RecordsFailed { get; set; }
    public int RowsInserted { get; set; }
    public int RowsSkipped { get; set; }
    public int FilesSaved { get; set; }
    public string Message { get; set; }
    public List<Dictionary<string, object>> PreviewRows { get; } = new();
    public List<object> Log { get; } = new();
}

// =============================================================================
// JSON path: select anywhere, set exactly
// =============================================================================

/// <summary>
/// $ = response root, @ = current loop item, .name, ['name'], [n], [*],
/// and ..name to find a node at any depth regardless of the hierarchy.
/// </summary>
public static class ApiJsonPath
{
    private abstract record Segment;
    private sealed record Property(string Name, bool Deep) : Segment;
    private sealed record Index(int Value) : Segment;
    private sealed record Wildcard(bool Deep) : Segment;

    public static bool IsValid(string path)
    {
        try { Parse(path); return true; }
        catch (FormatException) { return false; }
    }

    /// <summary>True for a single concrete location such as $.a.b[0].c.</summary>
    public static bool IsExactTarget(string path)
    {
        try
        {
            var (root, segments) = Parse(path);
            return root == '$' && segments.Count > 0 &&
                   segments.All(s => s is Index || s is Property { Deep: false });
        }
        catch (FormatException) { return false; }
    }

    private static (char Root, List<Segment> Segments) Parse(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new FormatException("The path is empty.");
        path = path.Trim();

        var root = path[0];
        if (root != '$' && root != '@') throw new FormatException("A path starts with $ or @.");

        var segments = new List<Segment>();
        var i = 1;

        while (i < path.Length)
        {
            if (path[i] == '.')
            {
                var deep = i + 1 < path.Length && path[i + 1] == '.';
                i += deep ? 2 : 1;

                if (i < path.Length && path[i] == '*') { segments.Add(new Wildcard(deep)); i++; continue; }

                var start = i;
                while (i < path.Length && path[i] != '.' && path[i] != '[') i++;
                if (i == start) throw new FormatException("A property name is missing.");
                segments.Add(new Property(path[start..i], deep));
            }
            else if (path[i] == '[')
            {
                var end = path.IndexOf(']', i);
                if (end < 0) throw new FormatException("A ']' is missing.");

                var inner = path[(i + 1)..end].Trim();
                if (inner == "*") segments.Add(new Wildcard(false));
                else if (inner.Length >= 2 && (inner[0] == '\'' || inner[0] == '"') && inner[^1] == inner[0])
                    segments.Add(new Property(inner[1..^1], false));
                else if (int.TryParse(inner, NumberStyles.None, CultureInfo.InvariantCulture, out var n))
                    segments.Add(new Index(n));
                else throw new FormatException($"[{inner}] is not supported.");

                i = end + 1;
            }
            else throw new FormatException($"Unexpected '{path[i]}' in the path.");
        }

        return (root, segments);
    }

    public static List<JsonNode> Select(JsonNode root, JsonNode item, string path)
    {
        var (start, segments) = Parse(path);
        var first = start == '@' ? item : root;
        IEnumerable<JsonNode> current = first == null ? Array.Empty<JsonNode>() : new[] { first };

        foreach (var segment in segments)
            current = Step(current, segment).ToList();

        return current.Where(n => n != null).ToList();
    }

    public static string Text(JsonNode node) => node switch
    {
        null => null,
        JsonValue v when v.TryGetValue<string>(out var s) => s,
        JsonValue v => v.ToJsonString(),
        _ => node.ToJsonString()
    };

    public static void Set(JsonNode root, string path, JsonNode value)
    {
        if (!IsExactTarget(path))
            throw new FormatException("A request target must be an exact path such as $.a.b[0].c.");

        var (_, segments) = Parse(path);
        var current = root;

        for (var i = 0; i < segments.Count; i++)
        {
            var last = i == segments.Count - 1;
            JsonNode Container() => segments[i + 1] is Index ? new JsonArray() : new JsonObject();

            switch (segments[i])
            {
                case Property p:
                    if (current is not JsonObject obj) throw new FormatException($"'{p.Name}' is not inside an object.");
                    if (last) { obj[p.Name] = value; return; }
                    obj[p.Name] ??= Container();
                    current = obj[p.Name];
                    break;

                case Index ix:
                    if (current is not JsonArray array) throw new FormatException($"[{ix.Value}] is not inside an array.");
                    while (array.Count <= ix.Value) array.Add(null);
                    if (last) { array[ix.Value] = value; return; }
                    array[ix.Value] ??= Container();
                    current = array[ix.Value];
                    break;
            }
        }
    }

    /// <summary>A copy for previews and stored samples: long strings (base64) are shortened.</summary>
    public static JsonNode Shrink(JsonNode node, int maxString = 300)
    {
        switch (node)
        {
            case null:
                return null;
            case JsonObject o:
                var copy = new JsonObject();
                foreach (var pair in o) copy[pair.Key] = Shrink(pair.Value, maxString);
                return copy;
            case JsonArray a:
                var list = new JsonArray();
                foreach (var entry in a.Take(20)) list.Add(Shrink(entry, maxString));
                return list;
            case JsonValue v when v.TryGetValue<string>(out var s) && s.Length > maxString:
                return JsonValue.Create(s[..60] + $"… ({s.Length:N0} characters)");
            default:
                return JsonNode.Parse(node.ToJsonString());
        }
    }

    private static IEnumerable<JsonNode> Step(IEnumerable<JsonNode> nodes, Segment segment)
    {
        foreach (var node in nodes)
        {
            switch (segment)
            {
                case Property { Deep: true } p:
                    foreach (var d in SelfAndDescendants(node))
                        if (d is JsonObject o && TryGet(o, p.Name, out var found)) yield return found;
                    break;

                case Property p:
                    if (node is JsonObject obj && TryGet(obj, p.Name, out var value)) yield return value;
                    break;

                case Index ix:
                    if (node is JsonArray arr && ix.Value < arr.Count) yield return arr[ix.Value];
                    break;

                case Wildcard w:
                    foreach (var source in w.Deep ? SelfAndDescendants(node) : new[] { node })
                    {
                        if (source is JsonArray a) foreach (var x in a) yield return x;
                        else if (source is JsonObject o) foreach (var pair in o) yield return pair.Value;
                    }
                    break;
            }
        }
    }

    private static bool TryGet(JsonObject obj, string name, out JsonNode value)
    {
        if (obj.TryGetPropertyValue(name, out value)) return true;
        foreach (var pair in obj)
        {
            if (string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase)) { value = pair.Value; return true; }
        }
        value = null;
        return false;
    }

    private static IEnumerable<JsonNode> SelfAndDescendants(JsonNode node)
    {
        if (node == null) yield break;
        yield return node;

        var children = node switch
        {
            JsonObject o => o.Select(p => p.Value),
            JsonArray a => a.AsEnumerable(),
            _ => Enumerable.Empty<JsonNode>()
        };

        foreach (var child in children)
            foreach (var d in SelfAndDescendants(child))
                yield return d;
    }
}

// =============================================================================
// Validation shared by the controller (save) and the engine (run)
// =============================================================================

public static class PipelineConfig
{
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static readonly string[] SystemValues = { "NewGuid", "UtcNow", "Now", "Today", "InflowDataID", "UseCaseID" };
    public static readonly string[] RecordValues = { "InflowDataID", "ReviewStatusCode", "ValidationStatusCode" };
    public static readonly string[] Operators = { "eq", "ne", "contains", "startsWith", "endsWith", "empty", "notEmpty", "in", "gt", "lt" };

    private static readonly Regex Name = new("^[A-Za-z0-9_.\\-]{1,100}$");

    
    private static readonly Regex StepKey = new("^[a-z][a-z0-9_]{0,29}$");
    private static readonly Regex FolderTemplate = new("^[A-Za-z0-9_\\-{}/ ]{0,300}$");
    private static readonly Regex FolderToken = new("\\{([A-Za-z0-9_]+)\\}");
    private static readonly string[] ProtectedHeaders = { "Host", "Content-Length", "Transfer-Encoding", "Authorization" };

    public static T Parse<T>(string json) where T : new() =>
        string.IsNullOrWhiteSpace(json) ? new T() : JsonSerializer.Deserialize<T>(json, Json) ?? new T();

    public static string ValidateSteps(List<PipelineStepConfig> steps, ISet<string> inflowFields)
    {
        if (steps.Count > 5) return "Use at most 5 steps.";

        var earlier = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var step in steps)
        {
            if (string.IsNullOrWhiteSpace(step.Key) || !StepKey.IsMatch(step.Key))
                return "Each step needs a key: lower-case letters, numbers or underscore, starting with a letter.";
            if (earlier.Contains(step.Key)) return $"Step key '{step.Key}' is used twice.";

            step.Kind = (step.Kind ?? "API").ToUpperInvariant();
            if (step.Kind is not ("API" or "ROWS")) return $"Step '{step.Key}': choose a step type.";

            if (step.Kind == "ROWS")
            {
                // A rows step reads stored rows; it has no connector and no body.
                if (step.SourceCollectionId <= 0) return $"Step '{step.Key}': choose the collection its rows come from.";
                step.Bindings = new List<PipelineBinding>();
                step.BodyMode = "NONE";
                step.ConnectorId = 0;
                step.ConnectorVersionId = 0;

                if (!string.IsNullOrWhiteSpace(step.LoopPath) && !ApiJsonPath.IsValid(step.LoopPath))
                    return $"Step '{step.Key}': the loop path is not valid.";

                var rowsScope = new HashSet<string>(earlier, StringComparer.OrdinalIgnoreCase) { step.Key };
                foreach (var c in step.ItemConditions)
                {
                    c.Step ??= step.Key;
                    var rowProblem = ValidateCondition(c, rowsScope, inflowFields);
                    if (rowProblem != null) return $"Step '{step.Key}' condition: {rowProblem}";
                }

                earlier.Add(step.Key);
                continue;
            }

            if (step.ConnectorId <= 0 || step.ConnectorVersionId <= 0) return $"Step '{step.Key}': choose a published connector.";

            step.BodyMode = (step.BodyMode ?? "JSON").ToUpperInvariant();
            if (step.BodyMode is not ("JSON" or "FORM" or "MULTIPART" or "NONE"))
                return $"Step '{step.Key}': choose a body type.";

            foreach (var b in step.Bindings)
            {
                b.In = (b.In ?? "body").ToLowerInvariant();
                b.Kind = (b.Kind ?? "text").ToLowerInvariant();
                var label = $"Step '{step.Key}', {b.In} '{b.Target}'";

                switch (b.In)
                {
                    case "body":
                        if (step.BodyMode != "JSON") return $"{label}: JSON body values need the JSON body type.";
                        if (!ApiJsonPath.IsExactTarget(b.Target)) return $"{label}: use an exact path such as $.a.b.c.";
                        break;
                    case "form":
                        if (step.BodyMode is not ("FORM" or "MULTIPART")) return $"{label}: form fields need a form body type.";
                        if (b.Kind is "file" or "inline" && step.BodyMode != "MULTIPART") return $"{label}: file fields need form-data.";
                        if (b.Kind == "inline")
                        {
                            if (string.IsNullOrWhiteSpace(b.FileName)) b.FileName = "inline.json";
                            if (!Name.IsMatch(b.FileName)) return $"{label}: enter a valid file name, such as evidence.json.";
                            if (string.IsNullOrWhiteSpace(b.ContentType)) b.ContentType = "application/json";
                            if (!MediaTypeHeaderValue.TryParse(b.ContentType, out _)) return $"{label}: enter a valid content type.";
                        }
                        goto case "query";
                    case "header":
                        if (ProtectedHeaders.Contains(b.Target, StringComparer.OrdinalIgnoreCase))
                            return $"{label}: this header is set by the connector and cannot be bound.";
                        goto case "query";
                    case "path":
                    case "query":
                        if (string.IsNullOrWhiteSpace(b.Target) || !Name.IsMatch(b.Target)) return $"{label}: enter a valid name.";
                        break;
                    default:
                        return $"Step '{step.Key}': unknown binding location '{b.In}'.";
                }

                var problem = ValidateSource(b.Source, b.Step, b.Value, earlier, inflowFields);
                if (problem != null) return $"{label}: {problem}";
            }

            if (!string.IsNullOrWhiteSpace(step.LoopPath) && !ApiJsonPath.IsValid(step.LoopPath))
                return $"Step '{step.Key}': the loop path is not valid.";

            var withSelf = new HashSet<string>(earlier, StringComparer.OrdinalIgnoreCase) { step.Key };
            foreach (var c in step.ItemConditions)
            {
                c.Step ??= step.Key;
                var problem = ValidateCondition(c, withSelf, inflowFields);
                if (problem != null) return $"Step '{step.Key}' condition: {problem}";
            }

            earlier.Add(step.Key);
        }

        return null;
    }

    public static string ValidateDriver(PipelineDriver driver, ISet<string> inflowFields)
    {
        driver.Match = driver.Match == "any" ? "any" : "all";
        foreach (var c in driver.Conditions)
        {
            if (c.Source == "json") return "Record conditions can use inflow fields or record values only.";
            var problem = ValidateCondition(c, new HashSet<string>(), inflowFields);
            if (problem != null) return $"Record condition: {problem}";
        }
        return null;
    }

    public static string ValidateMapping(PipelineMapping mapping, ISet<string> stepKeys, ISet<string> columns,
                                         ISet<string> inflowFields)
    {
        

        var mapped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var m in mapping.Columns)
        {
            if (string.IsNullOrWhiteSpace(m.Column) || !columns.Contains(m.Column))
                return $"'{m.Column}' is not a column of the collection.";
            if (!mapped.Add(m.Column)) return $"'{m.Column}' is mapped twice.";

            m.Source = (m.Source ?? "json").ToLowerInvariant();

            if (m.Source == "file")
            {
                var f = m.File;
                if (f == null) return $"'{m.Column}': complete the file settings.";

                var contentStep = f.ContentStep ?? f.Step;
                var nameStep = f.NameStep ?? f.Step;
                if (string.IsNullOrWhiteSpace(contentStep) || !stepKeys.Contains(contentStep))
                    return $"'{m.Column}': choose the step the file content comes from.";
                if (string.IsNullOrWhiteSpace(nameStep) || !stepKeys.Contains(nameStep))
                    return $"'{m.Column}': choose the step the file name comes from.";

                if (!ApiJsonPath.IsValid(f.ContentPath)) return $"'{m.Column}': the file content path is not valid.";
                if (!ApiJsonPath.IsValid(f.NamePath)) return $"'{m.Column}': the file name path is not valid.";
                if (!FolderTemplate.IsMatch(f.Folder ?? string.Empty)) return $"'{m.Column}': the folder may use letters, numbers, _ - / and {{Tokens}} only.";

                if (f.Parts.Count > 8) return $"'{m.Column}': use at most 8 folder levels.";
                var level = 0;
                foreach (var part in f.Parts)
                {
                    level++;
                    var partProblem = ValidateSource(part.Source, part.Step, part.Value, stepKeys, inflowFields);
                    if (partProblem != null) return $"'{m.Column}', folder level {level}: {partProblem}";
                }
                continue;
            }

            var problem = ValidateSource(m.Source, m.Step, m.Source == "json" ? m.Path : m.Value, stepKeys, inflowFields);
            if (problem != null) return $"'{m.Column}': {problem}";
        }

        // Folder tokens must name a mapped (non-file) column or the use case.
        foreach (var f in mapping.Columns.Where(c => c.Source == "file").Select(c => c.File))
        {
            foreach (Match token in FolderToken.Matches(f.Folder ?? string.Empty))
            {
                var name = token.Groups[1].Value;
                var known = name is "UseCaseName" or "UseCaseCode" ||
                            mapping.Columns.Any(c => c.Source != "file" && string.Equals(c.Column, name, StringComparison.OrdinalIgnoreCase));
                if (!known) return $"Folder token {{{name}}} is not a mapped column.";
            }
        }

        var dedupAllowed = new HashSet<string>(columns, StringComparer.OrdinalIgnoreCase) { "InflowDataID" };
        foreach (var d in mapping.Dedup)
            if (!dedupAllowed.Contains(d)) return $"Duplicate check column '{d}' is not in the collection.";

        return null;
    }

    private static string ValidateCondition(PipelineCondition c, ISet<string> steps, ISet<string> inflowFields)
    {
        if (!Operators.Contains(c.Op)) return $"'{c.Op}' is not a supported comparison.";
        return ValidateSource(c.Source, c.Step, c.Path, steps, inflowFields);
    }

    private static string ValidateSource(string source, string step, string value, ISet<string> steps, ISet<string> inflowFields)
    {
        switch ((source ?? string.Empty).ToLowerInvariant())
        {
            case "inflow":
                return value != null && inflowFields.Contains(value) ? null : $"'{value}' is not a field of the inflow template.";
            case "json":
                if (string.IsNullOrWhiteSpace(step) || !steps.Contains(step)) return "choose an earlier step.";
                return ApiJsonPath.IsValid(value) ? null : $"'{value}' is not a valid JSON path.";
            case "record":
                return RecordValues.Contains(value) ? null : "choose a record value.";
            case "rowid":
                return string.IsNullOrWhiteSpace(value) ? "choose the step whose row to link to." : null;
            case "system":
                return SystemValues.Contains(value) ? null : "choose a system value.";
            case "const":
                return (value?.Length ?? 0) <= 4000 ? null : "the constant is longer than 4000 characters.";
            default:
                return "choose a value source.";
        }
    }
}

// =============================================================================
// Engine
// =============================================================================

public sealed class ApiPipelineEngine
{
    private const int MaxInlineFileBytes = 10 * 1024 * 1024; 
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly Regex FolderToken = new("\\{([A-Za-z0-9_]+)\\}");

    private readonly ApiConfigDataAccess _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ApiConfigExecutionEngine _execution;
    private readonly IConfiguration _configuration;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ApiPipelineEngine> _logger;

    public ApiPipelineEngine(ApiConfigDataAccess db, IHttpClientFactory httpClientFactory,
                             ApiConfigExecutionEngine execution, IConfiguration configuration,
                             IServiceScopeFactory scopeFactory, ILogger<ApiPipelineEngine> logger)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
        _execution = execution;
        _configuration = configuration;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>The engine is a singleton, so scoped services are resolved per use.</summary>
    private T Use<T>(Func<ApsFileRoots, T> read)
    {
        using var scope = _scopeFactory.CreateScope();
        return read(scope.ServiceProvider.GetRequiredService<ApsFileRoots>());
    }

    // --- loaded definition --------------------------------------------------------

    private sealed class ConnectorDef
    {
        public string Method, BaseUrl, EndpointTemplate, PayloadTemplate;
        public int TimeoutSeconds;
        public List<(string Name, string Template)> Headers = new();
        public DataTable Query;
        public HashSet<string> Hosts = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<int> SuccessCodes = new();
    }

    private sealed class StepDef
    {
        public PipelineStepConfig Config;
        public ConnectorDef Connector;      // null for a rows step
        public bool IsRows => string.Equals(Config.Kind, "ROWS", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class PipelineDef
    {
        public int Id, UseCaseId, CollectionId, BatchSize;
        public string Status, UseCaseCode, UseCaseName;
        public PipelineDriver Driver;
        public PipelineMapping Mapping;
        public List<PipelineOutput> Outputs = new();
        public List<StepDef> Steps = new();
    }

    private sealed class Scope
    {
        public int InflowDataId, UseCaseId;
        public string RowDataJson, ReviewStatus, ValidationStatus;
        public Dictionary<string, string> Inflow;
        public Dictionary<string, (JsonNode Response, JsonNode Item)> Steps = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Identity of the last row written after each step, for later outputs to link to.</summary>
        public Dictionary<string, string> RowIds = new(StringComparer.OrdinalIgnoreCase);

        public Scope With(string key, JsonNode response, JsonNode item)
        {
            var copy = (Scope)MemberwiseClone();
            copy.Steps = new Dictionary<string, (JsonNode, JsonNode)>(Steps, StringComparer.OrdinalIgnoreCase)
            {
                [key] = (response, item)
            };
            copy.RowIds = new Dictionary<string, string>(RowIds, StringComparer.OrdinalIgnoreCase);
            return copy;
        }
    }

    private sealed class RecordResult
    {
        public int Rows, Inserted, Skipped, Files, Failures;
        public string FirstError;
        public void Fail(string message) { Failures++; FirstError ??= message; }
    }

    private sealed class RunContext
    {
        public int RunId;
        public bool IsTest;
        public string ActionBy;
        public PipelineRunSummary Summary;
    }

    private sealed class RowException : Exception
    {
        public RowException(string message) : base(message) { }
    }

    private sealed record CallResult(JsonNode Response, int? StatusCode, int DurationMs, string Error);

    // --- public entry points ------------------------------------------------------

    /// <summary>Runs one claimed or started run. A test run writes no rows, files or record state.</summary>
    public async Task<PipelineRunSummary> RunAsync(int runId, int pipelineId, bool isTest, string actionBy,
                                                   int? onlyInflowDataId, CancellationToken cancellationToken)
    {
        var summary = new PipelineRunSummary { RunId = runId };
        var run = new RunContext
        {
            RunId = runId,
            IsTest = isTest,
            ActionBy = Truncate(actionBy, 30),
            Summary = summary
        };

        try
        {
            var pipeline = LoadPipeline(pipelineId);
            if (pipeline.Steps.Count == 0)
                throw new InvalidOperationException("The pipeline has no API steps yet.");

            if (!isTest && pipeline.Status != "Published")
                throw new InvalidOperationException("The pipeline is not published.");

            var records = onlyInflowDataId.HasValue
                ? _db.Table("dbo.APS_InflowData_GetById", ApiConfigDataAccess.P("@InflowDataID", onlyInflowDataId.Value))
                : _db.Table("dbo.API_Config_Pipeline_GetDriverRecords",
                            ApiConfigDataAccess.P("@PipelineId", pipelineId),
                            ApiConfigDataAccess.P("@Top", isTest ? 3 : pipeline.BatchSize));

            foreach (DataRow record in records.Rows)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var scope = NewScope(pipeline, record);
                if (scope.UseCaseId != pipeline.UseCaseId) continue;

                summary.RecordsConsidered++;

                if (!Matches(pipeline.Driver.Conditions, pipeline.Driver.Match == "any", scope))
                {
                    Log(run, scope.InflowDataId, null, null, "Skipped", null, null, "The record does not meet the pipeline conditions.");
                    SetState(run, pipeline.Id, scope.InflowDataId, "Skipped", "Conditions not met.");
                    continue;
                }

                // The record is now in flight. This is what stamps the start time,
                // and it is what makes a run interrupted by an app pool recycle
                // show as Running rather than simply missing.
                SetState(run, pipeline.Id, scope.InflowDataId, "Running", null);

                var result = new RecordResult();
                await RunStepAsync(run, pipeline, 0, scope, result, cancellationToken);

                summary.RowsInserted += result.Inserted;
                summary.RowsSkipped += result.Skipped;
                summary.FilesSaved += result.Files;

                if (result.Failures == 0)
                {
                    summary.RecordsSucceeded++;
                    SetState(run, pipeline.Id, scope.InflowDataId, "Done",
                             $"{result.Rows} row(s), {result.Inserted} inserted, {result.Skipped} already present.");
                }
                else
                {
                    summary.RecordsFailed++;
                    SetState(run, pipeline.Id, scope.InflowDataId, "Failed", result.FirstError);
                }
            }

            // No outputs saved means the old shape: one output after the last step,
            // into the pipeline's own collection.
            pipeline.Outputs = pipeline.Mapping.Outputs is { Count: > 0 }
                ? pipeline.Mapping.Outputs
                : new List<PipelineOutput>
                  {
                  new()
                  {
                      Step = pipeline.Steps.Count > 0 ? pipeline.Steps[^1].Config.Key : null,
                      CollectionId = pipeline.CollectionId,
                      Columns = pipeline.Mapping.Columns,
                      Dedup = pipeline.Mapping.Dedup
                  }
                  };

            foreach (var output in pipeline.Outputs)
                if (output.CollectionId <= 0) output.CollectionId = pipeline.CollectionId;

            summary.Status = summary.RecordsFailed == 0 ? "Succeeded"
                : summary.RecordsSucceeded > 0 ? "PartiallyFailed" : "Failed";
            summary.Message = summary.RecordsConsidered == 0 ? "No records were waiting." : null;

            // Chaining. Once per run rather than once per record: a pipeline that
            // completed twenty records only needs its follower woken once.
            if (summary.RecordsSucceeded > 0)
                NudgeDependents(run, pipeline.Id);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            summary.Status = "Failed";
            summary.Message = "The run was stopped because the application is shutting down.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Pipeline run {RunId} failed.", runId);
            summary.Status = "Failed";
            summary.Message = Truncate(ex.Message, 1000);
        }
        finally
        {
            try
            {
                _db.Execute("dbo.API_Config_Pipeline_CompleteRun",
                    ApiConfigDataAccess.P("@PipelineRunId", runId),
                    ApiConfigDataAccess.P("@Status", summary.Status),
                    ApiConfigDataAccess.P("@RecordsConsidered", summary.RecordsConsidered),
                    ApiConfigDataAccess.P("@RecordsSucceeded", summary.RecordsSucceeded),
                    ApiConfigDataAccess.P("@RecordsFailed", summary.RecordsFailed),
                    ApiConfigDataAccess.P("@RowsInserted", summary.RowsInserted),
                    ApiConfigDataAccess.P("@RowsSkipped", summary.RowsSkipped),
                    ApiConfigDataAccess.P("@FilesSaved", summary.FilesSaved),
                    ApiConfigDataAccess.P("@Message", summary.Message));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Pipeline run {RunId} could not be closed.", runId);
            }
        }

        return summary;
    }

    /// <summary>
    /// Postman-style Send: calls the steps in order for one record, following
    /// the first loop item at each level, and returns what each call returned.
    /// Nothing is stored.
    /// </summary>
    public async Task<List<object>> PreviewAsync(List<PipelineStepConfig> steps, int useCaseId, int inflowDataId,
                                                 CancellationToken cancellationToken)
    {
        var record = ApiConfigDataAccess.First(
            _db.Table("dbo.APS_InflowData_GetById", ApiConfigDataAccess.P("@InflowDataID", inflowDataId)));
        if (record == null) throw new InvalidOperationException("The sample record was not found.");

        var pipeline = new PipelineDef { UseCaseId = useCaseId };
        var scope = NewScope(pipeline, record);
        if (scope.UseCaseId != useCaseId) throw new InvalidOperationException("The sample record belongs to another use case.");

        var results = new List<object>();

        foreach (var config in steps)
        {
            JsonNode response;
            List<JsonNode> items;
            string error = null;
            int? statusCode = null;
            var started = DateTime.UtcNow;

            if (string.Equals(config.Kind, "ROWS", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var rows = ReadCollectionRows(config.SourceCollectionId, inflowDataId);
                    response = rows;
                    items = string.IsNullOrWhiteSpace(config.LoopPath)
                        ? rows.Select(r => (JsonNode)r).ToList()
                        : ApiJsonPath.Select(rows, null, config.LoopPath);
                }
                catch (Exception ex) when (ex is SqlException or InvalidOperationException)
                {
                    response = null;
                    items = new List<JsonNode>();
                    error = Truncate(ex.Message, 500);
                }
            }
            else
            {
                var step = new StepDef { Config = config, Connector = LoadConnector(config.ConnectorVersionId) };
                var call = await CallAsync(step, scope, cancellationToken);

                response = call.Response;
                statusCode = call.StatusCode;
                error = call.Error;
                items = response == null || string.IsNullOrWhiteSpace(config.LoopPath)
                    ? new List<JsonNode> { response }
                    : ApiJsonPath.Select(response, null, config.LoopPath);
            }

            results.Add(new
            {
                step = config.Key,
                kind = config.Kind,
                statusCode,
                durationMs = (int)(DateTime.UtcNow - started).TotalMilliseconds,
                error,
                loopItems = items.Count,
                response = response == null ? null : ApiJsonPath.Shrink(response)?.ToJsonString()
            });

            if (error != null || items.Count == 0 || items[0] == null) break;
            scope = scope.With(config.Key, response, items[0]);
        }

        return results;
    }

    // --- steps, loops and rows ------------------------------------------------------

    private async Task RunStepAsync(RunContext run, PipelineDef pipeline, int index, Scope scope,
                                RecordResult result, CancellationToken cancellationToken)
    {
        var step = pipeline.Steps[index];
        var key = step.Config.Key;

        JsonNode response;
        List<JsonNode> items;

        if (step.IsRows)
        {
            // Rows already stored in a collection drive this step: one item per row.
            var started = DateTime.UtcNow;
            try
            {
                var rows = ReadCollectionRows(step.Config.SourceCollectionId, scope.InflowDataId);
                response = rows;
                items = string.IsNullOrWhiteSpace(step.Config.LoopPath)
                    ? rows.Select(r => (JsonNode)r).ToList()
                    : ApiJsonPath.Select(rows, null, step.Config.LoopPath);
            }
            catch (Exception ex) when (ex is SqlException or InvalidOperationException)
            {
                var failure = Truncate(ex.Message, 500);
                Log(run, scope.InflowDataId, key, null, "Failed", null,
                    (int)(DateTime.UtcNow - started).TotalMilliseconds, failure);
                result.Fail($"Step '{key}': {failure}");
                return;
            }

            Log(run, scope.InflowDataId, key, null, "Succeeded", null,
                (int)(DateTime.UtcNow - started).TotalMilliseconds, $"{items.Count} row(s) read.");
        }
        else
        {
            var call = await CallAsync(step, scope, cancellationToken);

            Log(run, scope.InflowDataId, key, null, call.Error == null ? "Succeeded" : "Failed",
                call.StatusCode, call.DurationMs, call.Error);

            if (call.Error != null)
            {
                result.Fail($"Step '{key}': {call.Error}");
                return;
            }

            response = call.Response;
            items = string.IsNullOrWhiteSpace(step.Config.LoopPath)
                ? new List<JsonNode> { call.Response }
                : ApiJsonPath.Select(call.Response, null, step.Config.LoopPath);
        }

        if (items.Count == 0)
        {
            Log(run, scope.InflowDataId, key, null, "Skipped", null, null,
                step.IsRows ? "No rows were found for this record." : "The loop path matched nothing in the response.");
            return;
        }

        for (var i = 0; i < items.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var itemScope = scope.With(key, response, items[i]);

            if (!Matches(step.Config.ItemConditions, false, itemScope))
            {
                Log(run, scope.InflowDataId, key, i, "Skipped", null, null, "The item does not meet the step conditions.");
                continue;
            }

            foreach (var output in pipeline.Outputs
                         .Where(o => string.Equals(o.Step, key, StringComparison.OrdinalIgnoreCase)))
                EmitRow(run, pipeline, output, itemScope, result, key, i);

            if (index + 1 < pipeline.Steps.Count)
                await RunStepAsync(run, pipeline, index + 1, itemScope, result, cancellationToken);
        }
    }

    /// <summary>One JSON object per collection row, so rows read like a response.</summary>
    private JsonArray ReadCollectionRows(int collectionId, int inflowDataId)
    {
        if (collectionId <= 0) throw new InvalidOperationException("This step has no source collection.");

        var table = _db.Table("dbo.APS_Collection_GetRowsForRecord",
            ApiConfigDataAccess.P("@CollectionID", collectionId),
            ApiConfigDataAccess.P("@InflowDataID", inflowDataId),
            ApiConfigDataAccess.P("@Top", 1000));

        var rows = new JsonArray();
        foreach (DataRow r in table.Rows)
        {
            var item = new JsonObject();
            foreach (DataColumn c in table.Columns)
                // Raw invariant text: these values feed API requests, file names and
                // columns, so they must not be formatted for display.
                item[c.ColumnName] = r[c] == DBNull.Value
                    ? null
                    : JsonValue.Create(Convert.ToString(r[c], Inv) ?? string.Empty);
            rows.Add(item);
        }
        return rows;
    }
    private void EmitRow(RunContext run, PipelineDef pipeline, PipelineOutput output, Scope scope,
                     RecordResult result, string stepKey, int itemIndex)
    {
        try
        {
            var row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            // Automatic columns are skipped when the target table has no such column,
            // or when the pipeline maps that column itself.
            bool Mapped(string column) =>
                output.Columns.Any(m => string.Equals(m.Column, column, StringComparison.OrdinalIgnoreCase));

            if (output.AutoInflowDataId && !Mapped("InflowDataID")) row["InflowDataID"] = scope.InflowDataId;
            if (output.AutoUseCaseId && !Mapped("UseCaseID")) row["UseCaseID"] = pipeline.UseCaseId;
            if (output.AutoCreatedBy && !Mapped("CreatedBy")) row["CreatedBy"] = run.ActionBy;

            foreach (var map in output.Columns.Where(m => m.Source != "file"))
                row[map.Column] = ResolveSource(map.Source, map.Step, map.Source == "json" ? map.Path : map.Value, scope);

            foreach (var map in output.Columns.Where(m => m.Source == "file"))
                row[map.Column] = SaveFile(run, pipeline, map.File, scope, row, result);

            result.Rows++;

            if (run.IsTest)
            {
                if (run.Summary.PreviewRows.Count < 50)
                {
                    row["__step"] = stepKey;
                    row["__collectionId"] = output.CollectionId;
                    run.Summary.PreviewRows.Add(row);
                }
                Log(run, scope.InflowDataId, stepKey, itemIndex, "Succeeded", null, null, "Test: row prepared, not saved.");
                return;
            }

            var inserted = ApiConfigDataAccess.Out("@Inserted", SqlDbType.TinyInt);
            var message = ApiConfigDataAccess.Out("@Message", SqlDbType.VarChar, 400);
            var newId = ApiConfigDataAccess.Out("@NewId", SqlDbType.Int);

            _db.Execute("dbo.APS_Collection_InsertRow",
            ApiConfigDataAccess.P("@CollectionID", output.CollectionId),
            ApiConfigDataAccess.P("@RowJson", JsonSerializer.Serialize(row)),
            ApiConfigDataAccess.P("@DedupColumns", output.Dedup.Count == 0 ? null : string.Join(",", output.Dedup)),
            ApiConfigDataAccess.P("@Mode", string.IsNullOrWhiteSpace(output.Mode) ? "insert" : output.Mode),
            inserted, message, newId);

            // Inserted or skipped, the id lets a later output point at this row.
            if (newId.Value != null && newId.Value != DBNull.Value && !string.IsNullOrEmpty(output.Step))
                scope.RowIds[output.Step] = Convert.ToString(newId.Value, Inv);

            var text = Convert.ToString(message.Value);

            // 1 inserted, 2 updated by an upsert. Both are a successful write.
            if (inserted.Value != DBNull.Value && Convert.ToInt32(inserted.Value) is 1 or 2)
            {
                result.Inserted++;
                Log(run, scope.InflowDataId, stepKey, itemIndex, "Succeeded", null, null, text);
            }
            else if (text != null && text.StartsWith("Row already exists", StringComparison.Ordinal))
            {
                result.Skipped++;
                Log(run, scope.InflowDataId, stepKey, itemIndex, "Skipped", null, null, text);
            }
            else
            {
                result.Fail(text);
                Log(run, scope.InflowDataId, stepKey, itemIndex, "Failed", null, null, text);
            }
        }
        catch (RowException ex)
        {
            result.Fail(ex.Message);
            Log(run, scope.InflowDataId, stepKey, itemIndex, "Failed", null, null, ex.Message);
        }
    }

    // --- files --------------------------------------------------------------------------

    private string SaveFile(RunContext run, PipelineDef pipeline, PipelineFileMap file, Scope scope,
                            IDictionary<string, object> row, RecordResult result)
    {
        var base64 = ResolveSource("json", file.ContentStep ?? file.Step, file.ContentPath, scope);
        if (string.IsNullOrWhiteSpace(base64)) throw new RowException("The file content is empty.");

        byte[] bytes;
        try { bytes = Convert.FromBase64String(base64.Trim()); }
        catch (FormatException) { throw new RowException("The file content is not valid base64."); }

        var maxBytes = _configuration.GetValue("ApiConfigurator:PipelineMaxFileMB", 50) * 1024L * 1024L;
        if (bytes.Length > maxBytes) throw new RowException("The file is larger than the configured limit.");

        var rawName = ResolveSource("json", file.NameStep ?? file.Step, file.NamePath, scope);
        var fileName = SafeSegment(Path.GetFileName(rawName ?? string.Empty));
        if (fileName.Length == 0) fileName = Guid.NewGuid().ToString("N") + ".bin";

        var root = ResolveRoot(file.Root);

        List<string> segments;

        if (file.Parts is { Count: > 0 })
        {
            // Each level is resolved from a value, so the structure follows the data.
            segments = file.Parts
                .Select(part => SafeSegment(ResolveSource(part.Source, part.Step, part.Value, scope)))
                .Select(s => s.Length == 0 ? "_" : s)
                .ToList();
        }
        else
        {
            // {Token} is a mapped column of this row, or the use case name or code.
            var folder = FolderToken.Replace(file.Folder ?? string.Empty, m =>
            {
                var token = m.Groups[1].Value;
                var value = token switch
                {
                    "UseCaseName" => pipeline.UseCaseName,
                    "UseCaseCode" => pipeline.UseCaseCode,
                    _ => row.TryGetValue(token, out var v) ? Convert.ToString(v, Inv) : null
                };
                var segment = SafeSegment(value);
                return segment.Length == 0 ? "_" : segment;
            });

            segments = folder.Split('/', '\\').Select(SafeSegment).Where(s => s.Length > 0).ToList();
        }
        var directory = Path.GetFullPath(Path.Combine(new[] { root }.Concat(segments).ToArray()));

        if (!directory.Equals(root, StringComparison.OrdinalIgnoreCase) &&
            !directory.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new RowException("The folder resolves outside the collection file root.");

        var target = Path.Combine(directory, fileName);
        if (run.IsTest) return target;

        Directory.CreateDirectory(directory);

        // Same content already there: reuse it. Different content: keep both.
        if (File.Exists(target))
        {
            if (new FileInfo(target).Length == bytes.Length && File.ReadAllBytes(target).AsSpan().SequenceEqual(bytes))
                return target;

            var name = Path.GetFileNameWithoutExtension(fileName);
            var extension = Path.GetExtension(fileName);
            for (var n = 2; File.Exists(target); n++)
                target = Path.Combine(directory, $"{name} ({n}){extension}");
        }

        File.WriteAllBytes(target, bytes);
        result.Files++;
        return target;
    }

    private static string SafeSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(value.Where(c => !invalid.Contains(c)).ToArray()).Trim().Trim('.');

        if (clean is "" or "." or "..") return string.Empty;
        return clean.Length > 100 ? clean[..100] : clean;
    }
    /// <summary>The named roots this environment offers, for the screen's dropdown.</summary>
    /// <summary>The folders an Admin has configured, for the screen's dropdown.</summary>
    public IReadOnlyList<string> FileRootAliases() => Use(roots => roots.ActiveAliases());

    /// <summary>
    /// Folders are managed in the portal, but each one was checked on save to sit
    /// inside Aps:AllowedBaseFolders, and that is verified again here.
    /// </summary>
    private string ResolveRoot(string alias)
    {
        var path = Use(roots => roots.PathFor(alias));

        if (string.IsNullOrWhiteSpace(path))
            throw new RowException(string.IsNullOrWhiteSpace(alias)
                ? "No default folder is configured. Add one in Administration, Folder Roots."
                : $"The folder '{alias}' is no longer configured.");

        if (!Use(roots => roots.IsInsideAllowedBase(path)))
            throw new RowException($"The folder '{alias ?? "default"}' is outside the permitted areas of this server.");

        return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
    // --- HTTP ---------------------------------------------------------------------------

    private async Task<CallResult> CallAsync(StepDef step, Scope scope, CancellationToken cancellationToken)
    {
        var started = DateTime.UtcNow;
        int Elapsed() => (int)(DateTime.UtcNow - started).TotalMilliseconds;

        try
        {
            var c = step.Connector;
            var bindings = step.Config.Bindings;
            var uid = Guid.NewGuid().ToString();

            // URL: {{name}} path bindings first, then the connector's own templates and query.
            var endpoint = c.EndpointTemplate ?? string.Empty;
            foreach (var b in bindings.Where(b => b.In == "path"))
                endpoint = endpoint.Replace("{{" + b.Target + "}}",
                    Uri.EscapeDataString(ResolveSource(b.Source, b.Step, b.Value, scope) ?? string.Empty));

            var url = ApiConfigPayloadResolver.BuildUrl(c.BaseUrl, endpoint, c.Query, scope.RowDataJson, uid, out _);

            foreach (var b in bindings.Where(b => b.In == "query"))
            {
                url += (url.Contains('?') ? "&" : "?") + Uri.EscapeDataString(b.Target) + "=" +
                       Uri.EscapeDataString(ResolveSource(b.Source, b.Step, b.Value, scope) ?? string.Empty);
            }

            var problem = CheckTarget(url, c);
            if (problem != null) return new CallResult(null, null, Elapsed(), problem);

            using var message = new HttpRequestMessage(new HttpMethod(string.IsNullOrWhiteSpace(c.Method) ? "POST" : c.Method), url);

            foreach (var (name, template) in c.Headers)
                message.Headers.TryAddWithoutValidation(name,
                    ApiConfigPayloadResolver.Resolve(template, scope.RowDataJson, uid, out _));

            foreach (var b in bindings.Where(b => b.In == "header"))
            {
                message.Headers.Remove(b.Target);
                message.Headers.TryAddWithoutValidation(b.Target, ResolveSource(b.Source, b.Step, b.Value, scope) ?? string.Empty);
            }

            message.Content = BuildBody(step, scope, uid);

            await _execution.AuthenticateAsync(message, step.Config.ConnectorVersionId, cancellationToken);

            var client = _httpClientFactory.CreateClient(ApiConfigExecutionEngine.HttpClientName);
            client.Timeout = TimeSpan.FromSeconds(Math.Clamp(c.TimeoutSeconds, 5, 300));

            using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var status = (int)response.StatusCode;

            var maxBytes = _configuration.GetValue("ApiConfigurator:PipelineMaxResponseMB", 50) * 1024L * 1024L;
            if (response.Content.Headers.ContentLength > maxBytes)
                return new CallResult(null, status, Elapsed(), "The response is larger than the configured limit.");

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
            {
                if (buffer.Length + read > maxBytes)
                    return new CallResult(null, status, Elapsed(), "The response is larger than the configured limit.");
                buffer.Write(chunk, 0, read);
            }

            var body = Encoding.UTF8.GetString(buffer.ToArray());
            var succeeded = c.SuccessCodes.Count > 0 ? c.SuccessCodes.Contains(status) : status is >= 200 and < 300;
            if (!succeeded)
                return new CallResult(null, status, Elapsed(), $"HTTP {status}: {Truncate(body, 300)}");

            JsonNode json;
            try { json = JsonNode.Parse(body); }
            catch (JsonException) { return new CallResult(null, status, Elapsed(), "The response is not JSON."); }

            return new CallResult(json, status, Elapsed(), null);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new CallResult(null, null, Elapsed(), "The call timed out.");
        }
        catch (Exception ex) when (ex is HttpRequestException or FormatException or JsonException or IOException
                                        or InvalidOperationException or UnauthorizedAccessException)
        {
            return new CallResult(null, null, Elapsed(), Truncate(ex.Message, 500));
        }
    }

    private HttpContent BuildBody(StepDef step, Scope scope, string uid)
    {
        var bindings = step.Config.Bindings;

        switch (step.Config.BodyMode)
        {
            case "NONE":
                return null;

            case "FORM":
                return new FormUrlEncodedContent(bindings.Where(b => b.In == "form")
                    .Select(b => new KeyValuePair<string, string>(b.Target, ResolveSource(b.Source, b.Step, b.Value, scope) ?? string.Empty)));

            case "MULTIPART":
                var multipart = new MultipartFormDataContent();
                foreach (var b in bindings.Where(b => b.In == "form"))
                {
                    var value = ResolveSource(b.Source, b.Step, b.Value, scope) ?? string.Empty;
                    if (b.Kind == "file")
                    {
                        var path = CheckUploadPath(value);
                        var part = new ByteArrayContent(File.ReadAllBytes(path));
                        part.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                        multipart.Add(part, b.Target, Path.GetFileName(path));
                    }
                    else if (b.Kind == "inline")
                    {
                        // The file is built from the resolved value and sent from memory.
                        // Nothing is written to disk, so there is nothing to clean up.
                        var bytes = Encoding.UTF8.GetBytes(value);
                        if (bytes.Length > MaxInlineFileBytes)
                            throw new InvalidOperationException(
                                $"Form field '{b.Target}': the inline file is larger than {MaxInlineFileBytes / (1024 * 1024)} MB.");

                        var part = new ByteArrayContent(bytes);
                        part.Headers.ContentType = new MediaTypeHeaderValue(
                            string.IsNullOrWhiteSpace(b.ContentType) ? "application/json" : b.ContentType);
                        multipart.Add(part, b.Target, string.IsNullOrWhiteSpace(b.FileName) ? "inline.json" : b.FileName);
                    }
                    else
                    {
                        multipart.Add(new StringContent(value), b.Target);
                    }
                }
                return multipart;

            default: // JSON
                var template = string.IsNullOrWhiteSpace(step.Connector.PayloadTemplate) ? "{}" : step.Connector.PayloadTemplate;
                var resolved = ApiConfigPayloadResolver.Resolve(template, scope.RowDataJson, uid, out _);
                var node = JsonNode.Parse(resolved) ?? new JsonObject();

                // Values are placed into the parsed document, never pasted into text.
                foreach (var b in bindings.Where(b => b.In == "body"))
                    ApiJsonPath.Set(node, b.Target, JsonValue.Create(ResolveSource(b.Source, b.Step, b.Value, scope)));

                return new StringContent(node.ToJsonString(), Encoding.UTF8, "application/json");
        }
    }

    private string CheckTarget(string url, ConnectorDef connector)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return "The target URL is not valid.";

        var allowHttp = _configuration.GetValue("ApiConfigurator:AllowNonHttpsTargets", false);
        if (uri.Scheme != Uri.UriSchemeHttps && !(allowHttp && uri.Scheme == Uri.UriSchemeHttp))
            return "The target must use HTTPS.";

        return connector.Hosts.Contains(uri.Host) ? null : $"'{uri.Host}' is not on the connector's allowed hosts.";
    }

    /// <summary>Files uploaded by a pipeline may only come from the document folders.</summary>
    private string CheckUploadPath(string value)
    {
        var full = Path.GetFullPath(value ?? string.Empty);

        // Files written by a pipeline live under a managed folder, so those count too.
        var allowed = Use(roots => roots.ReadableRoots())
            .Any(r => full.StartsWith(Path.GetFullPath(r).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                                      StringComparison.OrdinalIgnoreCase));

        if (!allowed) throw new InvalidOperationException("The upload file is outside the document folders.");
        if (!File.Exists(full)) throw new InvalidOperationException("The upload file was not found.");
        return full;
    }

    // --- values and conditions ----------------------------------------------------------

    private static string ResolveSource(string source, string step, string value, Scope s)
    {
        switch ((source ?? string.Empty).ToLowerInvariant())
        {
            case "inflow":
                return value != null && s.Inflow.TryGetValue(value, out var field) ? field : null;

            case "record":
                return value switch
                {
                    "InflowDataID" => s.InflowDataId.ToString(Inv),
                    "ReviewStatusCode" => s.ReviewStatus,
                    "ValidationStatusCode" => s.ValidationStatus,
                    _ => null
                };

            case "rowid":
                return value != null && s.RowIds.TryGetValue(value, out var rowId) ? rowId : null;

            case "json":
                if (step == null || !s.Steps.TryGetValue(step, out var context)) return null;   
                return ApiJsonPath.Text(ApiJsonPath.Select(context.Response, context.Item, value).FirstOrDefault());

            case "system":
                return value switch
                {
                    "NewGuid" => Guid.NewGuid().ToString(),
                    "UtcNow" => DateTime.UtcNow.ToString("o", Inv),
                    "Now" => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", Inv),
                    "Today" => DateTime.Today.ToString("yyyy-MM-dd", Inv),
                    "InflowDataID" => s.InflowDataId.ToString(Inv),
                    "UseCaseID" => s.UseCaseId.ToString(Inv),
                    _ => null
                };

            default: // const
                return value;
        }
    }

    private static bool Matches(List<PipelineCondition> conditions, bool any, Scope scope)
    {
        if (conditions == null || conditions.Count == 0) return true;

        var results = conditions.Select(c => Compare(ResolveSource(c.Source, c.Step, c.Path, scope), c.Op, c.Value));
        return any ? results.Any(r => r) : results.All(r => r);
    }

    private static bool Compare(string actual, string op, string expected)
    {
        var a = actual?.Trim() ?? string.Empty;
        var e = expected?.Trim() ?? string.Empty;

        switch (op)
        {
            case "eq": return string.Equals(a, e, StringComparison.OrdinalIgnoreCase);
            case "ne": return !string.Equals(a, e, StringComparison.OrdinalIgnoreCase);
            case "contains": return a.Contains(e, StringComparison.OrdinalIgnoreCase);
            case "startsWith": return a.StartsWith(e, StringComparison.OrdinalIgnoreCase);
            case "endsWith": return a.EndsWith(e, StringComparison.OrdinalIgnoreCase);
            case "empty": return a.Length == 0;
            case "notEmpty": return a.Length > 0;
            case "in": return e.Split(',').Any(x => string.Equals(a, x.Trim(), StringComparison.OrdinalIgnoreCase));
            case "gt":
            case "lt":
                if (!decimal.TryParse(a, NumberStyles.Any, Inv, out var x) || !decimal.TryParse(e, NumberStyles.Any, Inv, out var y))
                    return false;
                return op == "gt" ? x > y : x < y;
            default:
                return false;
        }
    }

    // --- loading --------------------------------------------------------------------------

    private PipelineDef LoadPipeline(int pipelineId)
    {
        var set = _db.DataSet("dbo.API_Config_Pipeline_Get", ApiConfigDataAccess.P("@PipelineId", pipelineId));
        var header = ApiConfigDataAccess.First(set.Tables[0]) ?? throw new InvalidOperationException("Pipeline not found.");

        var pipeline = new PipelineDef
        {
            Id = pipelineId,
            UseCaseId = ApiConfigDataAccess.Int(header, "UseCaseID"),
            CollectionId = ApiConfigDataAccess.Int(header, "CollectionID"),
            BatchSize = ApiConfigDataAccess.Int(header, "BatchSize", 50),
            Status = ApiConfigDataAccess.Str(header, "Status"),
            Driver = PipelineConfig.Parse<PipelineDriver>(ApiConfigDataAccess.Str(header, "DriverJson")),
            Mapping = PipelineConfig.Parse<PipelineMapping>(ApiConfigDataAccess.Str(header, "MappingJson"))
        };

        foreach (DataRow r in set.Tables[1].Rows)
        {
            var kind = ApiConfigDataAccess.Str(r, "StepKind");
            var config = new PipelineStepConfig
            {
                Key = ApiConfigDataAccess.Str(r, "StepKey"),
                Kind = string.IsNullOrWhiteSpace(kind) ? "API" : kind.ToUpperInvariant(),
                SourceCollectionId = ApiConfigDataAccess.Int(r, "SourceCollectionId"),
                ConnectorId = ApiConfigDataAccess.Int(r, "ConnectorId"),
                ConnectorVersionId = ApiConfigDataAccess.Int(r, "ConnectorVersionId"),
                BodyMode = ApiConfigDataAccess.Str(r, "BodyMode"),
                Bindings = PipelineConfig.Parse<List<PipelineBinding>>(ApiConfigDataAccess.Str(r, "BindingsJson")),
                LoopPath = ApiConfigDataAccess.Str(r, "LoopPath"),
                ItemConditions = PipelineConfig.Parse<List<PipelineCondition>>(ApiConfigDataAccess.Str(r, "ItemConditionsJson"))
            };

            // Only an API step needs a connector loaded.
            pipeline.Steps.Add(new StepDef
            {
                Config = config,
                Connector = config.Kind == "ROWS" ? null : LoadConnector(config.ConnectorVersionId)
            });
        }
        // No outputs saved means the old shape: one output after the last step,
        // into the pipeline's own collection.
        pipeline.Outputs = pipeline.Mapping.Outputs is { Count: > 0 }
            ? pipeline.Mapping.Outputs
            : new List<PipelineOutput>
              {
                  new()
                  {
                      Step = pipeline.Steps.Count > 0 ? pipeline.Steps[^1].Config.Key : null,
                      CollectionId = pipeline.CollectionId,
                      Columns = pipeline.Mapping.Columns,
                      Dedup = pipeline.Mapping.Dedup
                  }
              };

        foreach (var output in pipeline.Outputs)
            if (output.CollectionId <= 0) output.CollectionId = pipeline.CollectionId;
        var useCase = _db.Table("dbo.APS_UseCaseMaster_Get",
                ApiConfigDataAccess.P("@ClientID", null), ApiConfigDataAccess.P("@ProcessID", null),
                ApiConfigDataAccess.P("@UserName", null))
            .AsEnumerable()
            .FirstOrDefault(r => ApiConfigDataAccess.Int(r, "UseCaseID") == pipeline.UseCaseId);

        pipeline.UseCaseCode = useCase == null ? string.Empty : ApiConfigDataAccess.Str(useCase, "UseCaseCode");
        pipeline.UseCaseName = useCase == null ? string.Empty : ApiConfigDataAccess.Str(useCase, "UseCaseName");

        return pipeline;
    }

    private ConnectorDef LoadConnector(int connectorVersionId)
    {
        var set = _db.DataSet("dbo.API_Config_ConnectorVersion_Get",
                              ApiConfigDataAccess.P("@ConnectorVersionId", connectorVersionId));
        var version = ApiConfigDataAccess.First(set.Tables[0])
                      ?? throw new InvalidOperationException($"Connector version {connectorVersionId} was not found.");

        var connector = new ConnectorDef
        {
            Method = ApiConfigDataAccess.Str(version, "HttpMethod"),
            BaseUrl = ApiConfigDataAccess.Str(version, "BaseUrl"),
            EndpointTemplate = ApiConfigDataAccess.Str(version, "EndpointTemplate"),
            PayloadTemplate = ApiConfigDataAccess.Str(version, "PayloadTemplateJson"),
            TimeoutSeconds = ApiConfigDataAccess.Int(version, "TimeoutSeconds", 30),
            Query = set.Tables.Count > 2 ? set.Tables[2] : new DataTable()
        };

        if (set.Tables.Count > 1)
            foreach (DataRow h in set.Tables[1].Rows)
                connector.Headers.Add((ApiConfigDataAccess.Str(h, "HeaderName"), ApiConfigDataAccess.Str(h, "HeaderValueTemplate")));

        if (set.Tables.Count > 3)
            foreach (DataRow h in set.Tables[3].Rows)
                connector.Hosts.Add(ApiConfigDataAccess.Str(h, "HostName"));

        if (set.Tables.Count > 4)
            foreach (DataRow s in set.Tables[4].Rows)
                if (int.TryParse(ApiConfigDataAccess.Str(s, "StatusCode"), out var code)) connector.SuccessCodes.Add(code);

        return connector;
    }

    private static Scope NewScope(PipelineDef pipeline, DataRow record)
    {
        var rowJson = ApiConfigDataAccess.Str(record, "RowDataJson");
        var inflow = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            if (!string.IsNullOrWhiteSpace(rowJson) && JsonNode.Parse(rowJson) is JsonObject obj)
                foreach (var pair in obj) inflow[pair.Key] = ApiJsonPath.Text(pair.Value);
        }
        catch (JsonException) { }

        var useCaseId = record.Table.Columns.Contains("UseCaseID")
            ? ApiConfigDataAccess.Int(record, "UseCaseID")
            : pipeline.UseCaseId;

        return new Scope
        {
            InflowDataId = ApiConfigDataAccess.Int(record, "InflowDataID"),
            UseCaseId = useCaseId,
            RowDataJson = rowJson,
            ReviewStatus = ApiConfigDataAccess.Str(record, "ReviewStatusCode"),
            ValidationStatus = ApiConfigDataAccess.Str(record, "ValidationStatusCode"),
            Inflow = inflow
        };
    }

    // --- logging ---------------------------------------------------------------------------

    private void Log(RunContext run, int? inflowDataId, string stepKey, int? itemIndex, string outcome,
                     int? statusCode, int? durationMs, string message)
    {
        if (run.IsTest && run.Summary.Log.Count < 200)
            run.Summary.Log.Add(new { inflowDataId, stepKey, itemIndex, outcome, statusCode, durationMs, message });

        try
        {
            _db.Execute("dbo.API_Config_Pipeline_LogItem",
                ApiConfigDataAccess.P("@PipelineRunId", run.RunId),
                ApiConfigDataAccess.P("@InflowDataID", inflowDataId),
                ApiConfigDataAccess.P("@StepKey", stepKey),
                ApiConfigDataAccess.P("@ItemIndex", itemIndex),
                ApiConfigDataAccess.P("@Outcome", outcome),
                ApiConfigDataAccess.P("@HttpStatusCode", statusCode),
                ApiConfigDataAccess.P("@DurationMs", durationMs),
                ApiConfigDataAccess.P("@Message", Truncate(message, 1000)));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Pipeline run item could not be logged.");
        }
    }
    /// <summary>
    /// Brings forward any pipeline configured to follow this one, so the
    /// scheduler claims it on its next poll instead of at its next scheduled
    /// time.
    ///
    /// Chaining deliberately stays a scheduling decision rather than an inline
    /// call: each pipeline in a chain keeps its own lease, its own run log and
    /// its own failure, so a chain of five agents is five resumable runs rather
    /// than one long transaction that cannot be restarted in the middle.
    /// </summary>
    private void NudgeDependents(RunContext run, int pipelineId)
    {
        if (run.IsTest) return;

        try
        {
            _db.Execute("dbo.API_Config_Pipeline_NudgeDependents",
                ApiConfigDataAccess.P("@PipelineId", pipelineId));
        }
        catch (Exception ex)
        {
            // A chain that cannot be brought forward still runs on its own
            // schedule, so this must never fail the records that just succeeded.
            _logger.LogError(ex, "Dependent pipelines of {PipelineId} could not be nudged.", pipelineId);
        }
    }
    /// <summary>
    /// The numeric companion to the text status, so a screen or a report can
    /// filter without parsing words, and so a new status word cannot silently
    /// change the meaning of an existing code.
    ///   1 = running, 0 = completed, 2 = failed, 3 = skipped
    /// </summary>
    private static byte StatusCodeFor(string status) => status switch
    {
        "Running" => (byte)1,
        "Done" => (byte)0,
        "Failed" => (byte)2,
        _ => (byte)3
    };

    private void SetState(RunContext run, int pipelineId, int inflowDataId, string status, string message)
    {
        if (run.IsTest) return;

        _db.Execute("dbo.API_Config_Pipeline_SetRecordState",
            ApiConfigDataAccess.P("@PipelineId", pipelineId),
            ApiConfigDataAccess.P("@InflowDataID", inflowDataId),
            ApiConfigDataAccess.P("@Status", status),
            ApiConfigDataAccess.P("@PipelineRunId", run.RunId),
            ApiConfigDataAccess.P("@Message", Truncate(message, 1000)),
            ApiConfigDataAccess.P("@StatusCode", StatusCodeFor(status)));
    }

    private static string Truncate(string value, int length) =>
        string.IsNullOrEmpty(value) || value.Length <= length ? value : value[..length];
}

// =============================================================================
// Scheduler
// =============================================================================

public sealed class ApiPipelineWorker : BackgroundService
{
    private readonly ApiPipelineEngine _engine;
    private readonly ApiConfigDataAccess _db;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ApiPipelineWorker> _logger;

    public ApiPipelineWorker(ApiPipelineEngine engine, ApiConfigDataAccess db,
                             IConfiguration configuration, ILogger<ApiPipelineWorker> logger)
    {
        _engine = engine;
        _db = db;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_configuration.GetValue("ApiConfigurator:PipelinesEnabled", true)) return;

        var seconds = Math.Max(10, _configuration.GetValue("ApiConfigurator:PipelinePollSeconds", 30));
        var runBy = "pipeline-" + Environment.MachineName;
        if (runBy.Length > 30) runBy = runBy[..30];

        while (!stoppingToken.IsCancellationRequested)
        {
            var claimed = false;

            try
            {
                var runId = ApiConfigDataAccess.Out("@PipelineRunId", SqlDbType.Int);
                var pipelineId = ApiConfigDataAccess.Out("@PipelineId", SqlDbType.Int);

                _db.Execute("dbo.API_Config_Pipeline_ClaimDue",
                    ApiConfigDataAccess.P("@RunBy", runBy),
                    ApiConfigDataAccess.P("@LeaseSeconds", 3600),
                    runId, pipelineId);

                var id = runId.Value is int r ? r : 0;
                if (id > 0)
                {
                    claimed = true;
                    await _engine.RunAsync(id, (int)pipelineId.Value, false, runBy, null, stoppingToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Pipeline scheduler poll failed.");
            }

            if (claimed) continue;   // look straight away for the next due pipeline

            try { await Task.Delay(TimeSpan.FromSeconds(seconds), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
using System.Data;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgenticPitstop.Infrastructure;

// =============================================================================
// SOP Analyzer
//
// A developer uploads a Standard Operating Procedure; an agent returns the
// automation footprint and the exact human-in-the-loop checkpoints. What used
// to be an hours-long read-through becomes a blueprint in minutes.
//
// The agent is behind an interface with two implementations - a built-in sample
// that needs no connection, and the real DICE Comprehender call. Which one runs
// is a configuration value, so the screen can be demonstrated before the agent
// is wired up, and a third provider later is one class and one registration.
// =============================================================================

// -----------------------------------------------------------------------------
// The agent's response, mirroring the SopAnalyzerResult schema exactly
// -----------------------------------------------------------------------------

public sealed class SopSystemRef
{
    [JsonPropertyName("name")] public string Name { get; set; }
    [JsonPropertyName("direction")] public string Direction { get; set; }   // Source | Target
}

public sealed class SopStep
{
    [JsonPropertyName("stepNumber")] public int StepNumber { get; set; }
    [JsonPropertyName("description")] public string Description { get; set; }

    /// <summary>Deterministic - API | Deterministic - RPA | AI Agent | Unclear</summary>
    [JsonPropertyName("actionType")] public string ActionType { get; set; }

    [JsonPropertyName("systems")] public List<SopSystemRef> Systems { get; set; } = new();
    [JsonPropertyName("documentExtraction")] public string DocumentExtraction { get; set; }  // Yes | No
    [JsonPropertyName("hitlRequired")] public string HitlRequired { get; set; }        // Yes | No | Optional
    [JsonPropertyName("reasoning")] public string Reasoning { get; set; }

    /// <summary>True for a step the agent judged rather than executed by contract.</summary>
    [JsonIgnore]
    public bool IsAiDriven =>
        (ActionType ?? string.Empty).StartsWith("AI", StringComparison.OrdinalIgnoreCase);
}

public sealed class SopSystems
{
    [JsonPropertyName("source")] public List<string> Source { get; set; } = new();
    [JsonPropertyName("target")] public List<string> Target { get; set; } = new();

    /// <summary>Distinct systems across both directions - one system can be both.</summary>
    [JsonIgnore]
    public int DistinctCount =>
        Source.Concat(Target)
              .Where(s => !string.IsNullOrWhiteSpace(s))
              .Distinct(StringComparer.OrdinalIgnoreCase)
              .Count();
}

public sealed class SopSummary
{
    [JsonPropertyName("totalSteps")] public int TotalSteps { get; set; }
    [JsonPropertyName("hitlRequiredCount")] public int HitlRequiredCount { get; set; }
    [JsonPropertyName("hitlOptionalCount")] public int HitlOptionalCount { get; set; }
    [JsonPropertyName("documentExtractionInvolved")] public bool DocumentExtractionInvolved { get; set; }
    [JsonPropertyName("documentExtractionStepCount")] public int DocumentExtractionStepCount { get; set; }
    [JsonPropertyName("deterministicPercent")] public int DeterministicPercent { get; set; }
    [JsonPropertyName("aiPercent")] public int AiPercent { get; set; }
    [JsonPropertyName("complexityLevel")] public string ComplexityLevel { get; set; }
    [JsonPropertyName("complexityScore")] public int ComplexityScore { get; set; }
    [JsonPropertyName("complexityFactors")] public List<string> ComplexityFactors { get; set; } = new();
}

public sealed class SopAnalyzerResult
{
    [JsonPropertyName("documentName")] public string DocumentName { get; set; }
    [JsonPropertyName("customer")] public string Customer { get; set; }
    [JsonPropertyName("useCase")] public string UseCase { get; set; }
    [JsonPropertyName("analyzedOn")] public DateTime? AnalyzedOn { get; set; }
    [JsonPropertyName("summary")] public SopSummary Summary { get; set; } = new();
    [JsonPropertyName("systems")] public SopSystems Systems { get; set; } = new();
    [JsonPropertyName("steps")] public List<SopStep> Steps { get; set; } = new();

    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static SopAnalyzerResult Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        try { return JsonSerializer.Deserialize<SopAnalyzerResult>(json, Json); }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                "The agent returned something that is not the expected blueprint: " + ex.Message);
        }
    }

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    /// <summary>
    /// Recomputes the summary from the steps when the agent left it incomplete.
    /// The screen's tiles must never contradict the table underneath them, so
    /// the steps are treated as the truth and the summary as a convenience.
    /// </summary>
    public void Reconcile()
    {
        Summary ??= new SopSummary();
        Steps ??= new List<SopStep>();

        if (Steps.Count == 0) return;

        Summary.TotalSteps = Steps.Count;

        Summary.HitlRequiredCount = Steps.Count(s =>
            string.Equals(s.HitlRequired, "Yes", StringComparison.OrdinalIgnoreCase));

        Summary.HitlOptionalCount = Steps.Count(s =>
            string.Equals(s.HitlRequired, "Optional", StringComparison.OrdinalIgnoreCase));

        Summary.DocumentExtractionStepCount = Steps.Count(s =>
            string.Equals(s.DocumentExtraction, "Yes", StringComparison.OrdinalIgnoreCase));

        Summary.DocumentExtractionInvolved = Summary.DocumentExtractionStepCount > 0;

        var aiSteps = Steps.Count(s => s.IsAiDriven);
        Summary.AiPercent = (int)Math.Round(aiSteps * 100.0 / Steps.Count, MidpointRounding.AwayFromZero);
        Summary.DeterministicPercent = 100 - Summary.AiPercent;

        if (Summary.ComplexityScore <= 0)
            Summary.ComplexityScore = Score(aiSteps);

        if (string.IsNullOrWhiteSpace(Summary.ComplexityLevel))
            Summary.ComplexityLevel = Summary.ComplexityScore >= 67 ? "High"
                                    : Summary.ComplexityScore >= 34 ? "Medium"
                                    : "Low";

        if (Summary.ComplexityFactors.Count == 0)
        {
            Summary.ComplexityFactors.Add($"{Systems.DistinctCount} distinct systems");
            if (Summary.DocumentExtractionStepCount > 0)
                Summary.ComplexityFactors.Add($"{Summary.DocumentExtractionStepCount} unstructured document step(s)");
            Summary.ComplexityFactors.Add($"{Summary.HitlRequiredCount} of {Steps.Count} steps require HITL");
        }
    }

    /// <summary>
    /// The same four factors the instruction prompt asks the agent to weigh, so
    /// a locally computed score is comparable with one the agent returned.
    /// </summary>
    private int Score(int aiSteps)
    {
        var systems = Math.Min(Systems.DistinctCount, 6) / 6.0 * 30;
        var ai = aiSteps * 1.0 / Math.Max(Steps.Count, 1) * 30;
        var extraction = Summary.DocumentExtractionInvolved ? 15 : 0;
        var hitl = Summary.HitlRequiredCount * 1.0 / Math.Max(Steps.Count, 1) * 25;

        return Math.Clamp((int)Math.Round(systems + ai + extraction + hitl), 0, 100);
    }
}

// -----------------------------------------------------------------------------
// The agent seam
// -----------------------------------------------------------------------------

public sealed record SopAnalysisRequest(
    string DocumentName,
    string ContentType,
    byte[] Content,
    string Customer,
    string UseCaseName,
    string AnalysisDepth);

/// <summary>
/// Turns an uploaded SOP into a blueprint. One implementation calls DICE; the
/// other returns a worked example so the screen is usable before the agent is
/// connected. Adding a different comprehender means adding a class here and one
/// line in the module below.
/// </summary>
public interface ISopAnalyzerAgent
{
    string Name { get; }

    Task<SopAnalyzerResult> AnalyzeAsync(SopAnalysisRequest request, CancellationToken cancellationToken);
}

// -----------------------------------------------------------------------------
// Sample agent - no connection required
// -----------------------------------------------------------------------------

/// <summary>
/// Returns the worked example from the SOP Analyzer specification, with the
/// uploaded document's own name and context substituted in.
///
/// This exists so the screen can be shown and tested before DICE is reachable.
/// It is never silent about what it is: the screen labels the result as a
/// sample, so nobody mistakes it for a real analysis.
/// </summary>
public sealed class SopAnalyzerSampleAgent : ISopAnalyzerAgent
{
    public string Name => "Sample blueprint";

    public Task<SopAnalyzerResult> AnalyzeAsync(SopAnalysisRequest request, CancellationToken cancellationToken)
    {
        var result = new SopAnalyzerResult
        {
            DocumentName = request.DocumentName,
            Customer = request.Customer,
            UseCase = request.UseCaseName,
            AnalyzedOn = DateTime.UtcNow,
            Systems = new SopSystems
            {
                Source = new List<string> { "Imaging Portal", "LifeSys", "Document Repository" },
                Target = new List<string> { "LifeSys", "Imaging Portal" }
            },
            Steps = new List<SopStep>
            {
                Step(1, "Receive inbound request from Imaging queue",
                     "Deterministic - API", "Imaging Portal", "Source", "No", "No",
                     "Fixed polling API call, fully rule-based, no interpretation involved."),

                Step(2, "Download attached PDF/TIFF evidence documents",
                     "Deterministic - RPA", "Document Repository", "Source", "Yes", "No",
                     "Mechanical file download via screen automation; no judgement applied."),

                Step(3, "Extract policy, insured and claim fields from unstructured PDF",
                     "AI Agent", "DICE Extraction Agent", "Source", "Yes", "Yes",
                     "AI performs free-form extraction from an unstructured document with no fixed layout, so the output must be verified before it feeds downstream decisions."),

                Step(4, "Classify product type (Group Accident / Hospital Indemnity / Cancer)",
                     "AI Agent", "DICE Classification Agent", "Source", "No", "Yes",
                     "Classification confidence falls below the auto-accept threshold often enough that a reviewer must confirm the category."),

                Step(5, "Look up policy and financial snapshot",
                     "Deterministic - API", "LifeSys", "Source", "No", "No",
                     "Direct, parameterised API call with a defined contract, so the response mapping is unambiguous."),

                Step(6, "Run eligibility and benefit-tier checklist rules",
                     "Deterministic - RPA", "Checklist Engine", "Source", "No", "No",
                     "Fixed SQL comparison against enrollment data; the same inputs always produce the same outcome."),

                Step(7, "Generate PASS/FAIL recommendation with justification",
                     "AI Agent", "DICE Recommendation Agent", "Source", "No", "Yes",
                     "AI synthesises a natural-language justification from several sources, so a human must approve the reasoning before it is treated as final."),

                Step(8, "Post approved disposition back to policy admin system",
                     "Deterministic - API", "LifeSys", "Target", "No", "No",
                     "Mechanical write-back of an already-approved value using a fixed API contract, with no new judgement at this step."),

                Step(9, "Update correspondence system with outcome letter",
                     "Deterministic - RPA", "Imaging Portal", "Target", "No", "No",
                     "Template-based letter generation and filing; fully scripted, no interpretation.")
            }
        };

        result.Reconcile();
        return Task.FromResult(result);
    }

    private static SopStep Step(int number, string description, string actionType,
                                string system, string direction, string extraction,
                                string hitl, string reasoning) => new()
                                {
                                    StepNumber = number,
                                    Description = description,
                                    ActionType = actionType,
                                    Systems = new List<SopSystemRef> { new() { Name = system, Direction = direction } },
                                    DocumentExtraction = extraction,
                                    HitlRequired = hitl,
                                    Reasoning = reasoning
                                };
}

// -----------------------------------------------------------------------------
// DICE agent
// -----------------------------------------------------------------------------

/// <summary>
/// Calls the DICE Comprehender agent with the instruction prompt and the
/// response schema from the SOP Analyzer specification.
///
/// The endpoint and the client identifier come from configuration; the secret
/// is resolved at the moment of use through the same resolver the API
/// Configurator uses, so no credential is held here, in the database, or in
/// the browser.
/// </summary>
public sealed class SopAnalyzerDiceAgent : ISopAnalyzerAgent
{
    public const string HttpClientName = "ApsSopAnalyzer";

    private readonly IHttpClientFactory _factory;
    private readonly IConfiguration _configuration;
    private readonly ApiConfigSecretResolver _secrets;
    private readonly ILogger<SopAnalyzerDiceAgent> _logger;

    public SopAnalyzerDiceAgent(IHttpClientFactory factory,
                                IConfiguration configuration,
                                ApiConfigSecretResolver secrets,
                                ILogger<SopAnalyzerDiceAgent> logger)
    {
        _factory = factory;
        _configuration = configuration;
        _secrets = secrets;
        _logger = logger;
    }

    public string Name => "DICE Comprehender";

    public async Task<SopAnalyzerResult> AnalyzeAsync(SopAnalysisRequest request,
                                                      CancellationToken cancellationToken)
    {
        var endpoint = _configuration["SopAnalyzer:Endpoint"];
        if (string.IsNullOrWhiteSpace(endpoint))
            throw new InvalidOperationException(
                "No SOP Analyzer endpoint is configured. Set SopAnalyzer:Endpoint, or switch the provider to Sample.");

        using var content = new MultipartFormDataContent
        {
            { new StringContent(InstructionPrompt, Encoding.UTF8), "InputGoal" },
            { new StringContent(ResponseSchema, Encoding.UTF8), "OutputFormat" },
            { new StringContent(request.AnalysisDepth ?? "Standard", Encoding.UTF8), "AnalysisDepth" }
        };

        var file = new ByteArrayContent(request.Content);
        file.Headers.TryAddWithoutValidation("Content-Type",
            string.IsNullOrWhiteSpace(request.ContentType) ? "application/octet-stream" : request.ContentType);
        content.Add(file, "File", request.DocumentName);

        using var message = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = content };

        var clientId = _configuration["SopAnalyzer:ClientIdHeader"];
        var clientIdValue = _configuration["SopAnalyzer:ClientId"];
        if (!string.IsNullOrWhiteSpace(clientId) && !string.IsNullOrWhiteSpace(clientIdValue))
            message.Headers.TryAddWithoutValidation(clientId, clientIdValue);

        var secretRef = _configuration["SopAnalyzer:SecretRef"];
        if (!string.IsNullOrWhiteSpace(secretRef))
        {
            var secret = await _secrets.ResolveAsync("sop-analyzer", secretRef, cancellationToken);
            if (string.IsNullOrWhiteSpace(secret))
                throw new InvalidOperationException($"The secret '{secretRef}' is not configured in this environment.");

            var header = _configuration["SopAnalyzer:SecretHeader"] ?? "Authorization";
            message.Headers.TryAddWithoutValidation(header, secret);
        }

        using var client = _factory.CreateClient(HttpClientName);
        using var response = await client.SendAsync(message, cancellationToken);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("SOP Analyzer agent returned {Status}.", (int)response.StatusCode);
            throw new InvalidOperationException(
                $"The agent returned HTTP {(int)response.StatusCode}: {Truncate(body, 300)}");
        }

        var result = SopAnalyzerResult.Parse(Unwrap(body))
                     ?? throw new InvalidOperationException("The agent returned an empty response.");

        result.DocumentName ??= request.DocumentName;
        result.Customer ??= request.Customer;
        result.UseCase ??= request.UseCaseName;
        result.AnalyzedOn ??= DateTime.UtcNow;
        result.Reconcile();

        return result;
    }

    /// <summary>
    /// Agents commonly wrap the payload in an envelope. The blueprint is taken
    /// from whichever property actually holds it rather than assuming the body
    /// is the blueprint itself.
    /// </summary>
    private static string Unwrap(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return body;

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return body;

            if (document.RootElement.TryGetProperty("steps", out _)) return body;

            foreach (var name in new[] { "result", "data", "response", "output" })
            {
                if (!document.RootElement.TryGetProperty(name, out var inner)) continue;

                return inner.ValueKind == JsonValueKind.String
                    ? inner.GetString()
                    : inner.GetRawText();
            }
        }
        catch (JsonException)
        {
            // Not JSON at all; let the caller's parse produce the clearer message.
        }

        return body;
    }

    private static string Truncate(string value, int length) =>
        string.IsNullOrEmpty(value) || value.Length <= length ? value : value[..length];

    // -------------------------------------------------------------------------
    // The agreed prompt and schema, kept beside the call that uses them
    // -------------------------------------------------------------------------

    public const string InstructionPrompt = """
You are analyzing a Standard Operating Procedure (SOP) document for a Human-in-the-Loop (HITL)
automation platform called Agentic PITSTOP. Read the entire document and extract the operational
process as a sequence of discrete steps, then classify each step and summarize the overall process.

For EACH step in the SOP, identify:
1. Step number (sequential order as it appears in the procedure).
2. A concise one-line description of what the step does.
3. Action type - classify as exactly one of:
   - "Deterministic - API": a system-to-system call with a fixed, known request/response contract.
   - "Deterministic - RPA": a scripted, rule-based automation with no interpretation required.
   - "AI Agent": any step where an AI/ML agent performs judgement-based work with no fixed code path.
4. System(s) involved - name every source and/or target system mentioned or implied for this step.
   Note the direction: Source (data is read from it) or Target (data is written to it).
5. Document extraction involved - "Yes" if the step reads data out of an uploaded or downloaded
   document (PDF, TIFF, DOCX, scanned image); otherwise "No".
6. HITL required - decide "Yes", "No" or "Optional" using this rule set, in priority order:
   a. If action type is "Deterministic - API" or "Deterministic - RPA" then HITL = No.
   b. If action type is "AI Agent" AND its output is posted, actioned, or used to make a downstream
      decision then HITL = Yes.
   c. If action type is "AI Agent" AND its output is used only as read-only reference and is never
      posted or actioned then HITL = Optional.
7. Reasoning - one sentence explaining why HITL was or was not required, referencing the rule applied.

After classifying all steps, produce a PROCESS SUMMARY: total steps; count of steps requiring HITL
and the count that are optional; whether document extraction is involved and from how many steps;
distinct source and target systems, deduplicated; the deterministic versus AI split as whole
percentages of total steps; and a complexity rating of "Low", "Medium" or "High" with a numeric
score from 0 to 100, derived from the number of distinct systems, the proportion of AI Agent steps,
the presence of unstructured document extraction, and the proportion of steps requiring HITL.

Rules and constraints:
- Do not invent systems, steps, or integrations that are not stated or clearly implied in the SOP.
  If a system name is ambiguous, use a generic label and do not fabricate a proper noun.
- If the SOP does not state whether a step is API-based or RPA-based, use "Deterministic - RPA" only
  when a UI-driven or manual action is described; otherwise mark the action type "Unclear" and say so
  in the reasoning for human confirmation.
- Preserve the original order of steps; do not reorder or merge them unless the SOP explicitly
  describes them as one combined action.
- Output must be valid JSON only, conforming exactly to the schema provided, with no commentary,
  markdown, or explanation outside the JSON object.
""";

    public const string ResponseSchema = """
{
  "$schema": "http://json-schema.org/draft-07/schema#",
  "title": "SopAnalyzerResult",
  "type": "object",
  "required": ["documentName", "summary", "steps", "systems"],
  "properties": {
    "documentName": { "type": "string" },
    "customer": { "type": "string" },
    "useCase": { "type": "string" },
    "analyzedOn": { "type": "string", "format": "date-time" },
    "summary": {
      "type": "object",
      "required": ["totalSteps", "hitlRequiredCount", "hitlOptionalCount", "documentExtractionInvolved",
                   "deterministicPercent", "aiPercent", "complexityLevel", "complexityScore"],
      "properties": {
        "totalSteps": { "type": "integer" },
        "hitlRequiredCount": { "type": "integer" },
        "hitlOptionalCount": { "type": "integer" },
        "documentExtractionInvolved": { "type": "boolean" },
        "documentExtractionStepCount": { "type": "integer" },
        "deterministicPercent": { "type": "integer", "minimum": 0, "maximum": 100 },
        "aiPercent": { "type": "integer", "minimum": 0, "maximum": 100 },
        "complexityLevel": { "type": "string", "enum": ["Low", "Medium", "High"] },
        "complexityScore": { "type": "integer", "minimum": 0, "maximum": 100 },
        "complexityFactors": { "type": "array", "items": { "type": "string" } }
      }
    },
    "systems": {
      "type": "object",
      "required": ["source", "target"],
      "properties": {
        "source": { "type": "array", "items": { "type": "string" } },
        "target": { "type": "array", "items": { "type": "string" } }
      }
    },
    "steps": {
      "type": "array",
      "items": {
        "type": "object",
        "required": ["stepNumber", "description", "actionType", "systems",
                     "documentExtraction", "hitlRequired", "reasoning"],
        "properties": {
          "stepNumber": { "type": "integer" },
          "description": { "type": "string" },
          "actionType": { "type": "string",
                          "enum": ["Deterministic - API", "Deterministic - RPA", "AI Agent", "Unclear"] },
          "systems": {
            "type": "array",
            "items": {
              "type": "object",
              "properties": {
                "name": { "type": "string" },
                "direction": { "type": "string", "enum": ["Source", "Target"] }
              }
            }
          },
          "documentExtraction": { "type": "string", "enum": ["Yes", "No"] },
          "hitlRequired": { "type": "string", "enum": ["Yes", "No", "Optional"] },
          "reasoning": { "type": "string" }
        }
      }
    }
  }
}
""";
}

// -----------------------------------------------------------------------------
// The service the controller uses
// -----------------------------------------------------------------------------

public sealed class SopAnalyzerService
{
    public const long MaxUploadBytes = 10 * 1024 * 1024;

    private static readonly string[] AllowedExtensions = { ".docx", ".pdf", ".xlsx" };

    private readonly ApsDb _db;
    private readonly ApsFileRoots _roots;
    private readonly IEnumerable<ISopAnalyzerAgent> _agents;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SopAnalyzerService> _logger;

    public SopAnalyzerService(ApsDb db, ApsFileRoots roots,
                              IEnumerable<ISopAnalyzerAgent> agents,
                              IConfiguration configuration,
                              ILogger<SopAnalyzerService> logger)
    {
        _db = db;
        _roots = roots;
        _agents = agents;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Which agent is live. "Sample" until the DICE endpoint is configured, so a
    /// fresh environment shows a working screen rather than an error.
    /// </summary>
    public ISopAnalyzerAgent Agent
    {
        get
        {
            var wanted = _configuration["SopAnalyzer:Provider"];

            if (string.IsNullOrWhiteSpace(wanted))
                wanted = string.IsNullOrWhiteSpace(_configuration["SopAnalyzer:Endpoint"])
                    ? "Sample"
                    : "Dice";

            return string.Equals(wanted, "Dice", StringComparison.OrdinalIgnoreCase)
                ? _agents.OfType<SopAnalyzerDiceAgent>().Cast<ISopAnalyzerAgent>().FirstOrDefault()
                  ?? _agents.First()
                : _agents.OfType<SopAnalyzerSampleAgent>().Cast<ISopAnalyzerAgent>().FirstOrDefault()
                  ?? _agents.First();
        }
    }

    public bool IsSample => Agent is SopAnalyzerSampleAgent;

    public static string Validate(IFormFile file)
    {
        if (file == null || file.Length == 0) return "Choose an SOP document to analyze.";

        var extension = Path.GetExtension(file.FileName);
        if (!AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            return "Only .docx, .pdf and .xlsx documents are supported.";

        return file.Length > MaxUploadBytes
            ? "The document is larger than the 10 MB limit."
            : null;
    }

    /// <summary>
    /// Keeps a copy of the uploaded SOP beside the other platform files, so a
    /// blueprint can always be checked against the document it came from.
    /// Returns null when no folder root is configured - a missing copy must not
    /// stop the analysis, because the blueprint is the deliverable.
    /// </summary>
    public string TryStore(string documentName, byte[] content, int useCaseId)
    {
        try
        {
            var configured = _roots.PathFor(_configuration["SopAnalyzer:FileRoot"]);
            if (string.IsNullOrWhiteSpace(configured)) return null;

            var root = Path.GetFullPath(configured)
                           .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            var directory = Path.Combine(root, "sop", $"useCase-{useCaseId}",
                                         DateTime.UtcNow.ToString("yyyyMMdd"));

            var target = Path.GetFullPath(Path.Combine(directory,
                $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Safe(Path.GetFileName(documentName))}"));

            if (!target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || !_roots.IsInsideAllowedBase(target))
                return null;

            Directory.CreateDirectory(directory);
            File.WriteAllBytes(target, content);
            return target;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            _logger.LogWarning(ex, "The uploaded SOP could not be kept; the analysis continues.");
            return null;
        }
    }

    public async Task<SopAnalyzerResult> AnalyzeAsync(SopAnalysisRequest request,
                                                      CancellationToken cancellationToken)
    {
        var result = await Agent.AnalyzeAsync(request, cancellationToken);
        result?.Reconcile();
        return result;
    }

    // --- persistence -----------------------------------------------------------

    public (int Id, string Message) Start(int useCaseId, string documentName, string storedFileName,
                                          string customer, string processName, string depth, string actionBy)
    {
        var outputs = _db.ExecuteWithOutput("APS_SopAnalysis_Start", p =>
        {
            ApsDb.In(p, "@UseCaseID", useCaseId);
            ApsDb.In(p, "@DocumentName", documentName);
            ApsDb.In(p, "@StoredFileName", storedFileName);
            ApsDb.In(p, "@Customer", customer);
            ApsDb.In(p, "@ProcessName", processName);
            ApsDb.In(p, "@AnalysisDepth", depth);
            ApsDb.In(p, "@ActionBy", actionBy);
            ApsDb.Out(p, "@SopAnalysisID", SqlDbType.Int);
            ApsDb.Out(p, "@Message", SqlDbType.VarChar, 200);
        });

        var id = outputs.TryGetValue("SopAnalysisID", out var value) && value != null
            ? Convert.ToInt32(value)
            : 0;

        return (id, Convert.ToString(outputs.TryGetValue("Message", out var m) ? m : null));
    }

    public void Complete(int id, SopAnalyzerResult result, string actionBy)
    {
        _db.ExecuteWithOutput("APS_SopAnalysis_Complete", p =>
        {
            ApsDb.In(p, "@SopAnalysisID", id);
            ApsDb.In(p, "@Status", "Completed");
            ApsDb.In(p, "@ResultJson", result.ToJson());
            ApsDb.In(p, "@TotalSteps", result.Summary.TotalSteps);
            ApsDb.In(p, "@HitlRequiredCount", result.Summary.HitlRequiredCount);
            ApsDb.In(p, "@HitlOptionalCount", result.Summary.HitlOptionalCount);
            ApsDb.In(p, "@DocumentExtractionInvolved", result.Summary.DocumentExtractionInvolved ? (byte)1 : (byte)0);
            ApsDb.In(p, "@DeterministicPercent", result.Summary.DeterministicPercent);
            ApsDb.In(p, "@AiPercent", result.Summary.AiPercent);
            ApsDb.In(p, "@ComplexityLevel", result.Summary.ComplexityLevel);
            ApsDb.In(p, "@ComplexityScore", result.Summary.ComplexityScore);
            ApsDb.In(p, "@SystemCount", result.Systems.DistinctCount);
            ApsDb.In(p, "@ResultMessage", null);
            ApsDb.In(p, "@ActionBy", actionBy);
            ApsDb.Out(p, "@Success", SqlDbType.TinyInt);
            ApsDb.Out(p, "@Message", SqlDbType.VarChar, 200);
        });
    }

    public void Fail(int id, string reason, string actionBy)
    {
        if (id <= 0) return;

        try
        {
            _db.ExecuteWithOutput("APS_SopAnalysis_Complete", p =>
            {
                ApsDb.In(p, "@SopAnalysisID", id);
                ApsDb.In(p, "@Status", "Failed");
                ApsDb.In(p, "@ResultMessage", reason);
                ApsDb.In(p, "@ActionBy", actionBy);
                ApsDb.Out(p, "@Success", SqlDbType.TinyInt);
                ApsDb.Out(p, "@Message", SqlDbType.VarChar, 200);
            });
        }
        catch (Exception ex)
        {
            // The caller is already reporting a failure; losing the record of it
            // must not replace that message with a different one.
            _logger.LogError(ex, "Analysis {Id} could not be marked failed.", id);
        }
    }

    public DataTable History(int useCaseId, int top = 20) =>
        _db.Query("APS_SopAnalysis_Get", p =>
        {
            ApsDb.In(p, "@UseCaseID", useCaseId);
            ApsDb.In(p, "@Top", top);
        });

    public DataRow ById(int id) =>
        _db.QuerySingle("APS_SopAnalysis_GetById", p => ApsDb.In(p, "@SopAnalysisID", id));

    private static string Safe(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "sop.bin";

        var cleaned = new string(value.Trim()
            .Select(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '-')
            .ToArray()).Trim('-', '.');

        return cleaned.Length == 0 ? "sop.bin" : cleaned;
    }
}

/// <summary>
/// One-line registration. A new comprehender is added here and nowhere else.
/// </summary>
public static class SopAnalyzerModule
{
    public static IServiceCollection AddSopAnalyzer(this IServiceCollection services)
    {
        services.AddHttpClient(SopAnalyzerDiceAgent.HttpClientName, client =>
        {
            // An SOP is long and the agent reads all of it, so this is a slow call
            // by nature rather than a sign of trouble.
            client.Timeout = TimeSpan.FromMinutes(5);
        });

        services.AddScoped<ISopAnalyzerAgent, SopAnalyzerSampleAgent>();
        services.AddScoped<ISopAnalyzerAgent, SopAnalyzerDiceAgent>();

        services.AddScoped<SopAnalyzerService>();
        return services;
    }
}
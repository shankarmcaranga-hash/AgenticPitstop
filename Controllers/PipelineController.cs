using System.Data;
using System.Text.Json;
using AgenticPitstop.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace AgenticPitstop.Controllers;

/// <summary>
/// Collection Load Pipelines. Admin only: a published pipeline calls external
/// APIs and writes data unattended, so publishing is the approval.
/// </summary>
[ApsAuthorize(ApsIdentity.RoleAdmin)]
public sealed class PipelineController : ApsController
{
    private readonly ApiPipelineEngine _engine;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<PipelineController> _logger;

    public PipelineController(ApsDb db, ApsIdentity identity, ApsMetadata meta, ApiPipelineEngine engine,
                              IHostApplicationLifetime lifetime, ILogger<PipelineController> logger)
        : base(db, identity, meta)
    {
        _engine = engine;
        _lifetime = lifetime;
        _logger = logger;
    }

    protected override string ActiveScreenKey => "pipelines";

    // --- reading ------------------------------------------------------------------

    [HttpGet]
    public IActionResult Index()
    {
        ViewBag.UseCases = Db.Query("APS_UseCaseMaster_Get", p =>
        {
            ApsDb.In(p, "@ClientID", null);
            ApsDb.In(p, "@ProcessID", null);
            ApsDb.In(p, "@UserName", null);
        });
        return View();
    }

    /// <summary>The published request a step will send: method, URL, headers, query, body template.</summary>
    [HttpGet]
    public IActionResult ConnectorRequest(int versionId)
    {
        var set = Db.QueryMultiple("API_Config_ConnectorVersion_Get", p => ApsDb.In(p, "@ConnectorVersionId", versionId));
        if (set.Tables.Count == 0 || set.Tables[0].Rows.Count == 0) return Fail("Connector version not found.");

        var v = set.Tables[0].Rows[0];
        return Ok(null, new
        {
            method = ApsMetadata.Cell(v, "HttpMethod"),
            baseUrl = ApsMetadata.Cell(v, "BaseUrl"),
            endpoint = ApsMetadata.Cell(v, "EndpointTemplate"),
            payloadTemplate = ApsMetadata.Cell(v, "PayloadTemplateJson"),
            status = ApsMetadata.Cell(v, "Status"),
            headers = set.Tables.Count > 1
                ? set.Tables[1].AsEnumerable().Select(r => new { name = ApsMetadata.Cell(r, "HeaderName"), value = ApsMetadata.Cell(r, "HeaderValueTemplate") }).ToList()
                : null,
            query = set.Tables.Count > 2
                ? set.Tables[2].AsEnumerable().Select(r => new { name = ApsMetadata.Cell(r, "ParameterName"), value = ApsMetadata.Cell(r, "ParameterValueTemplate") }).ToList()
                : null
        });
    }

    /// <summary>Recent records of the use case, for Send and Test run.</summary>
    [HttpGet]
    public IActionResult SampleRecords(int useCaseId)
    {
        var rows = Db.Query("APS_InflowData_Get", p =>
        {
            ApsDb.In(p, "@UseCaseID", useCaseId);
            ApsDb.In(p, "@ReviewStatus", null);
            ApsDb.In(p, "@Top", 50);
        });

        var list = rows.AsEnumerable().Select(r =>
        {
            var preview = string.Empty;
            try
            {
                using var doc = JsonDocument.Parse(ApsMetadata.Cell(r, "RowDataJson"));
                preview = string.Join(" · ", doc.RootElement.EnumerateObject().Take(3).Select(x => x.Value.ToString()));
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException) { }

            var id = ApsMetadata.Cell(r, "InflowDataID");
            return new { id = Convert.ToInt32(id), label = "#" + id + (preview.Length > 0 ? " · " + preview : "") };
        }).ToList();

        return Ok(null, list);
    }

    [HttpGet]
    public IActionResult List() =>
        Ok(null, ApsMetadata.ToList(Db.Query("API_Config_Pipeline_GetList")));

    [HttpGet]
    public IActionResult Get(int id)
    {
        var set = Db.QueryMultiple("API_Config_Pipeline_Get", p => ApsDb.In(p, "@PipelineId", id));
        if (set.Tables.Count == 0 || set.Tables[0].Rows.Count == 0) return Fail("Pipeline not found.");

        return Ok(null, new
        {
            pipeline = ApsMetadata.ToDictionary(set.Tables[0].Rows[0]),
            steps = set.Tables.Count > 1 ? ApsMetadata.ToList(set.Tables[1]) : new List<Dictionary<string, object>>()
        });
    }

    /// <summary>Everything the pipeline screen needs to offer for a use case.</summary>
    [HttpGet]
    public IActionResult Catalog(int useCaseId)
    {
        var collections = Db.Query("APS_Collection_Get").AsEnumerable()
            .Where(r => ApsMetadata.Cell(r, "UseCaseID") == useCaseId.ToString() && ApsMetadata.Cell(r, "IsGenerated") == "1")
            .Select(r => new
            {
                id = Convert.ToInt32(r["CollectionID"]),
                name = ApsMetadata.Cell(r, "CollectionName"),
                table = ApsMetadata.Cell(r, "TableName")
            }).ToList();

        var inflowFields = Meta.InflowColumns(useCaseId).AsEnumerable()
            .Select(r => new { code = ApsMetadata.Cell(r, "FieldCode"), label = ApsMetadata.Cell(r, "ExcelColumnName") })
            .ToList();

        return Ok(null, new
        {
            collections,
            inflowFields,
            connectors = ApsMetadata.ToList(Db.Query("API_Config_Connector_GetPublishedList")),
            fileRoots = _engine.FileRootAliases(),
            systemValues = PipelineConfig.SystemValues,
            recordValues = PipelineConfig.RecordValues,
            operators = PipelineConfig.Operators
        });
    }

    [HttpGet]
    public IActionResult CollectionColumns(int collectionId)
    {
        var columns = Db.Query("APS_CollectionColumn_Get", p => ApsDb.In(p, "@CollectionID", collectionId));
        return Ok(null, ApsMetadata.ToList(columns));
    }

    [HttpGet]
    public IActionResult Runs(int id) =>
        Ok(null, ApsMetadata.ToList(Db.Query("API_Config_Pipeline_GetRuns", p =>
        {
            ApsDb.In(p, "@PipelineId", id);
            ApsDb.In(p, "@Top", 20);
        })));

    [HttpGet]
    public IActionResult RunItems(int runId) =>
        Ok(null, ApsMetadata.ToList(Db.Query("API_Config_Pipeline_GetRunItems", p => ApsDb.In(p, "@PipelineRunId", runId))));

    // --- saving -------------------------------------------------------------------

    [HttpPost]
    [HttpPost]
    public IActionResult Save(int pipelineId, string pipelineCode, string pipelineName, int useCaseId,
                              int collectionId, string stepsJson, string driverJson, string mappingJson,
                              string scheduleType, int? intervalMinutes, string dailyTime,
                              int batchSize = 50, byte maxRecordAttempts = 3)
    {
        pipelineCode = pipelineCode?.Trim();
        pipelineName = pipelineName?.Trim();

        if (string.IsNullOrEmpty(pipelineCode) ||
            !System.Text.RegularExpressions.Regex.IsMatch(pipelineCode, "^[A-Za-z0-9_\\-]{2,50}$"))
            return Fail("Pipeline code: 2-50 letters, numbers, _ or -.");
        if (string.IsNullOrEmpty(pipelineName) || pipelineName.Length > 150)
            return Fail("Enter a pipeline name of up to 150 characters.");
        if (useCaseId <= 0 || collectionId <= 0)
            return Fail("Choose the use case and the collection.");

        List<PipelineStepConfig> steps;
        PipelineDriver driver;
        PipelineMapping mapping;
        try
        {
            steps = PipelineConfig.Parse<List<PipelineStepConfig>>(stepsJson);
            driver = PipelineConfig.Parse<PipelineDriver>(driverJson);
            mapping = PipelineConfig.Parse<PipelineMapping>(mappingJson);
        }
        catch (JsonException)
        {
            return Fail("The pipeline settings could not be read.");
        }

        var inflowFields = InflowFieldSet(useCaseId);

        // The collections of this use case whose tables exist: outputs and rows steps both use this.
        var collections = Db.Query("APS_Collection_Get").AsEnumerable()
            .Where(r => ApsMetadata.Cell(r, "UseCaseID") == useCaseId.ToString()
                     && ApsMetadata.Cell(r, "IsGenerated") == "1")
            .ToDictionary(r => Convert.ToInt32(r["CollectionID"]), r => ApsMetadata.Cell(r, "CollectionName"));

        var problem = PipelineConfig.ValidateSteps(steps, inflowFields)
                      ?? PipelineConfig.ValidateDriver(driver, inflowFields);
        if (problem != null) return Fail(problem);

        foreach (var rowsStep in steps.Where(s => string.Equals(s.Kind, "ROWS", StringComparison.OrdinalIgnoreCase)))
            if (!collections.ContainsKey(rowsStep.SourceCollectionId))
                return Fail($"Step '{rowsStep.Key}': choose a collection of this use case whose table has been created.");

        var stepKeys = steps.Select(s => s.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Older pipelines have no outputs: treat them as one output after the last step.
        if (mapping.Outputs.Count == 0 && mapping.Columns.Count > 0)
        {
            mapping.Outputs.Add(new PipelineOutput
            {
                Step = steps.Count > 0 ? steps[^1].Key : null,
                CollectionId = collectionId,
                Columns = mapping.Columns,
                Dedup = mapping.Dedup
            });
            mapping.Columns = new List<PipelineColumnMap>();
            mapping.Dedup = new List<string>();
        }

        if (mapping.Outputs.Count == 0)
            return Fail("Add at least one output on the Mapping tab.");

        var knownRoots = _engine.FileRootAliases();
        foreach (var file in mapping.Outputs.SelectMany(o => o.Columns)
                     .Where(c => c.Source == "file" && c.File != null)
                     .Select(c => c.File))
        {
            if (!string.IsNullOrWhiteSpace(file.Root) && !knownRoots.Contains(file.Root))
                return Fail($"The file root '{file.Root}' is not configured on this server.");
        }

        var number = 0;
        foreach (var output in mapping.Outputs)
        {
            number++;
            if (output.CollectionId <= 0) output.CollectionId = collectionId;

            if (string.IsNullOrWhiteSpace(output.Step) || !stepKeys.Contains(output.Step))
                return Fail($"Output {number}: choose the step its rows come from.");

            if (!collections.ContainsKey(output.CollectionId))
                return Fail($"Output {number}: choose a collection of this use case whose table has been created.");

            var outputColumns = Db.Query("APS_CollectionColumn_Get", p => ApsDb.In(p, "@CollectionID", output.CollectionId))
                .AsEnumerable()
                .Where(r => ApsMetadata.Cell(r, "IsSystemColumn") != "1")
                .Select(r => ApsMetadata.Cell(r, "ColumnName"))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var single = new PipelineMapping { Columns = output.Columns, Dedup = output.Dedup };
            problem = PipelineConfig.ValidateMapping(single, stepKeys, outputColumns, inflowFields);
            if (problem != null) return Fail($"Output {number} ({collections[output.CollectionId]}): {problem}");
        }

        scheduleType = (scheduleType ?? "MANUAL").ToUpperInvariant();
        if (scheduleType is not ("MANUAL" or "INTERVAL" or "DAILY")) return Fail("Choose a schedule.");
        if (scheduleType == "INTERVAL" && intervalMinutes is not (>= 5 and <= 1440))
            return Fail("Run every 5 to 1440 minutes.");

        TimeSpan? daily = null;
        if (scheduleType == "DAILY")
        {
            if (!TimeSpan.TryParse(dailyTime, out var t) || t < TimeSpan.Zero || t >= TimeSpan.FromDays(1))
                return Fail("Choose the daily run time.");
            daily = t;
        }

        if (batchSize is < 1 or > 500) return Fail("Records per run must be between 1 and 500.");
        if (maxRecordAttempts is < 1 or > 10) return Fail("Retries per record must be between 1 and 10.");

        var stepTable = new DataTable();
        stepTable.Columns.Add("StepOrder", typeof(int));
        stepTable.Columns.Add("StepKey", typeof(string));
        stepTable.Columns.Add("StepKind", typeof(string));
        stepTable.Columns.Add("ConnectorId", typeof(int));
        stepTable.Columns.Add("ConnectorVersionId", typeof(int));
        stepTable.Columns.Add("SourceCollectionId", typeof(int));
        stepTable.Columns.Add("BodyMode", typeof(string));
        stepTable.Columns.Add("BindingsJson", typeof(string));
        stepTable.Columns.Add("LoopPath", typeof(string));
        stepTable.Columns.Add("ItemConditionsJson", typeof(string));
        stepTable.Columns.Add("SampleResponseJson", typeof(string));

        var order = 0;
        foreach (var s in steps)
        {
            stepTable.Rows.Add(++order, s.Key.ToLowerInvariant(), s.Kind, s.ConnectorId, s.ConnectorVersionId,
                s.SourceCollectionId, s.BodyMode,
                JsonSerializer.Serialize(s.Bindings, PipelineConfig.Json),
                string.IsNullOrWhiteSpace(s.LoopPath) ? DBNull.Value : s.LoopPath.Trim(),
                s.ItemConditions.Count == 0 ? DBNull.Value : JsonSerializer.Serialize(s.ItemConditions, PipelineConfig.Json),
                string.IsNullOrWhiteSpace(s.SampleResponseJson) ? DBNull.Value : s.SampleResponseJson);
        }

        var outputs = Db.ExecuteWithOutput("API_Config_Pipeline_Save", p =>
        {
            ApsDb.In(p, "@PipelineId", pipelineId);
            ApsDb.In(p, "@PipelineCode", pipelineCode);
            ApsDb.In(p, "@PipelineName", pipelineName);
            ApsDb.In(p, "@UseCaseID", useCaseId);
            ApsDb.In(p, "@CollectionID", collectionId);
            ApsDb.In(p, "@DriverJson", JsonSerializer.Serialize(driver, PipelineConfig.Json));
            ApsDb.In(p, "@MappingJson", JsonSerializer.Serialize(mapping, PipelineConfig.Json));
            ApsDb.In(p, "@ScheduleType", scheduleType);
            ApsDb.In(p, "@IntervalMinutes", scheduleType == "INTERVAL" ? intervalMinutes : null);
            ApsDb.In(p, "@DailyTime", daily);
            ApsDb.In(p, "@BatchSize", batchSize);
            ApsDb.In(p, "@MaxRecordAttempts", maxRecordAttempts);
            ApsDb.Table(p, "@Steps", "dbo.API_Config_PipelineStepType", stepTable);
            ApsDb.In(p, "@ActionBy", ActionBy);
            ApsDb.Out(p, "@NewPipelineId", SqlDbType.Int);
            ApsDb.Out(p, "@Message", SqlDbType.VarChar, 200);
        });

        return FromProcedure(outputs, "NewPipelineId");
    }

    [HttpPost]
    public IActionResult Publish(int pipelineId)
    {
        var outputs = Db.ExecuteWithOutput("API_Config_Pipeline_Publish", p =>
        {
            ApsDb.In(p, "@PipelineId", pipelineId);
            ApsDb.In(p, "@ActionBy", ActionBy);
            ApsDb.Out(p, "@Success", SqlDbType.TinyInt);
            ApsDb.Out(p, "@Message", SqlDbType.VarChar, 200);
        });
        return FromProcedure(outputs);
    }

    [HttpPost]
    public IActionResult SetStatus(int pipelineId, string status)
    {
        var outputs = Db.ExecuteWithOutput("API_Config_Pipeline_SetStatus", p =>
        {
            ApsDb.In(p, "@PipelineId", pipelineId);
            ApsDb.In(p, "@Status", status);
            ApsDb.In(p, "@ActionBy", ActionBy);
            ApsDb.Out(p, "@Success", SqlDbType.TinyInt);
            ApsDb.Out(p, "@Message", SqlDbType.VarChar, 200);
        });
        return FromProcedure(outputs);
    }

    // --- running ------------------------------------------------------------------

    /// <summary>Postman-style Send for the unsaved steps, against one sample record.</summary>
    [HttpPost]
    public async Task<IActionResult> Send(int useCaseId, int inflowDataId, string stepsJson, CancellationToken cancellationToken)
    {
        List<PipelineStepConfig> steps;
        try { steps = PipelineConfig.Parse<List<PipelineStepConfig>>(stepsJson); }
        catch (JsonException) { return Fail("The step settings could not be read."); }

        var problem = PipelineConfig.ValidateSteps(steps, InflowFieldSet(useCaseId));
        if (problem != null) return Fail(problem);

        try
        {
            return Ok("Sent.", await _engine.PreviewAsync(steps, useCaseId, inflowDataId, cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return Fail(ex.Message);
        }
    }

    /// <summary>Runs the saved pipeline for up to 3 records, or one chosen record. Writes nothing.</summary>
    [HttpPost]
    public async Task<IActionResult> TestRun(int pipelineId, int? inflowDataId)
    {
        var runId = StartRun(pipelineId, "TEST", out var message);
        if (runId == 0) return Fail(message);

        var summary = await _engine.RunAsync(runId, pipelineId, true, ActionBy, inflowDataId, HttpContext.RequestAborted);
        return Ok($"Test run finished: {summary.Status}.", summary);
    }

    /// <summary>Starts a real run now, in the background.</summary>
    [HttpPost]
    public IActionResult Run(int pipelineId)
    {
        var runId = StartRun(pipelineId, "MANUAL", out var message);
        if (runId == 0) return Fail(message);

        var actionBy = ActionBy;   // read now: the request is gone once the task runs
        _ = Task.Run(async () =>
        {
            try { await _engine.RunAsync(runId, pipelineId, false, actionBy, null, _lifetime.ApplicationStopping); }
            catch (Exception ex) { _logger.LogError(ex, "Manual pipeline run {RunId} failed.", runId); }
        });

        return Ok("Run started. Refresh the run history to follow it.", new { runId });
    }

    // -------------------------------------------------------------------------------

    private int StartRun(int pipelineId, string trigger, out string message)
    {
        var outputs = Db.ExecuteWithOutput("API_Config_Pipeline_StartRun", p =>
        {
            ApsDb.In(p, "@PipelineId", pipelineId);
            ApsDb.In(p, "@TriggerType", trigger);
            ApsDb.In(p, "@RunBy", ActionBy);
            ApsDb.In(p, "@LeaseSeconds", 3600);
            ApsDb.Out(p, "@PipelineRunId", SqlDbType.Int);
            ApsDb.Out(p, "@Message", SqlDbType.VarChar, 200);
        });

        message = Convert.ToString(outputs.TryGetValue("Message", out var m) ? m : null);
        return outputs.TryGetValue("PipelineRunId", out var id) && id != null ? Convert.ToInt32(id) : 0;
    }

    private HashSet<string> InflowFieldSet(int useCaseId) =>
        Meta.InflowColumns(useCaseId).AsEnumerable()
            .Select(r => ApsMetadata.Cell(r, "FieldCode"))
            .Where(c => !string.IsNullOrEmpty(c))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
}
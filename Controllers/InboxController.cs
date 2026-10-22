using System.Data;
using System.Data;
using System.Text.Json;
using System.Text.RegularExpressions;
using AgenticPitstop.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace AgenticPitstop.Controllers;

[ApsAuthorize]
public sealed class InboxController : ApsController
{
    private readonly ApsDetailRuntime _detail;
    private readonly ILogger<InboxController> _logger;

    private readonly ApiPipelineEngine _pipeline;

    public InboxController(ApsDb db, ApsIdentity identity, ApsMetadata meta,
                           ApsDetailRuntime detail, ApiPipelineEngine pipeline, ILogger<InboxController> logger)
        : base(db, identity, meta)
    {
        _detail = detail;
        _pipeline = pipeline;
        _logger = logger;
    }

    protected override string ActiveScreenKey => "inbox";

    [HttpGet]
    public IActionResult Index(int useCaseId = 0, string status = null, string search = null)
    {
        var useCases = LoadUseCases();
        var allowedUseCaseIds = useCases.AsEnumerable()
            .Select(r => ToInt(r, "UseCaseID"))
            .Where(id => id > 0)
            .ToHashSet();

        if (useCaseId == 0 && allowedUseCaseIds.Count == 1)
            useCaseId = allowedUseCaseIds.First();

        if (useCaseId > 0 && !allowedUseCaseIds.Contains(useCaseId))
            useCaseId = 0;

        ViewBag.UseCases = useCases;
        ViewBag.UseCaseId = useCaseId;
        ViewBag.Search = search;
        ViewBag.Status = status;

        if (useCaseId <= 0)
        {
            ViewBag.Kpi = new DataTable();
            ViewBag.DataColumns = new List<Dictionary<string, string>>();
            ViewBag.Records = new List<Dictionary<string, object>>();
            ViewBag.StatusRows = new DataTable();
            ViewBag.StatusColumns = new List<string>();
            return View();
        }

        ViewBag.Kpi = Db.Query("APS_InflowData_GetKpi", p => ApsDb.In(p, "@UseCaseID", useCaseId));

        // The active template decides which fields the uploaded rows contain.
        var templateSet = Db.QueryMultiple("APS_InflowConfig_GetActive", p => ApsDb.In(p, "@UseCaseID", useCaseId));
        var templateColumns = templateSet.Tables.Count > 1 ? templateSet.Tables[1] : new DataTable();

        var templateLabels = templateColumns.AsEnumerable()
            .Where(r => !string.IsNullOrWhiteSpace(ToStringValue(r, "FieldCode")))
            .GroupBy(r => ToStringValue(r, "FieldCode"), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => ToStringValue(g.First(), "ExcelColumnName"),
                          StringComparer.OrdinalIgnoreCase);

        var inboxConfigSets = Db.QueryMultiple("APS_InboxConfig_Get", p => ApsDb.In(p, "@UseCaseID", useCaseId));
        var columns = inboxConfigSets.Tables.Count > 1 ? inboxConfigSets.Tables[1] : new DataTable();

        // Saved Inbox layout, limited to fields the current template still has.
        var dataColumns = columns.AsEnumerable()
            .Where(r => ToInt(r, "GridType") == 1)
            .OrderBy(r => ToInt(r, "SortOrder"))
            .Where(r => templateLabels.ContainsKey(ToStringValue(r, "FieldCode")))
            .Select(r => NewColumn(ToStringValue(r, "FieldCode"),
                                   string.IsNullOrWhiteSpace(ToStringValue(r, "DisplayName"))
                                       ? templateLabels[ToStringValue(r, "FieldCode")]
                                       : ToStringValue(r, "DisplayName")))
            .GroupBy(c => c["FieldCode"], StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        // No saved layout, or a new template replaced every field: show the template itself.
        if (dataColumns.Count == 0)
        {
            dataColumns = templateColumns.AsEnumerable()
                .Where(r => ToBool(r, "ShowInInbox"))
                .OrderBy(r => ToInt(r, "SortOrder"))
                .Select(r => NewColumn(ToStringValue(r, "FieldCode"), ToStringValue(r, "ExcelColumnName")))
                .Where(c => !string.IsNullOrWhiteSpace(c["FieldCode"]))
                .ToList();
        }
        //var dataColumns = columns.AsEnumerable()
        //    .Where(r => ToInt(r, "GridType") == 1)
        //    .OrderBy(r => ToInt(r, "SortOrder"))
        //    .Select(r => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        //    {
        //        ["FieldCode"] = ToStringValue(r, "FieldCode"),
        //        ["DisplayName"] = string.IsNullOrWhiteSpace(ToStringValue(r, "DisplayName"))
        //            ? ToStringValue(r, "FieldCode")
        //            : ToStringValue(r, "DisplayName")
        //    })
        //    .Where(c => !string.IsNullOrWhiteSpace(c["FieldCode"]))
        //    .ToList();

        var reviewStatus = string.IsNullOrWhiteSpace(status) || string.Equals(status, "All", StringComparison.OrdinalIgnoreCase)
            ? null
            : status.Trim();

        var inflowRows = Db.Query("APS_InflowData_Get", p =>
        {
            ApsDb.In(p, "@UseCaseID", useCaseId);
            ApsDb.In(p, "@ReviewStatus", reviewStatus);
            ApsDb.In(p, "@Top", 500);
        });

        var records = new List<Dictionary<string, object>>();
        foreach (DataRow row in inflowRows.Rows)
        {
            var flattened = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["InflowDataID"] = ToInt(row, "InflowDataID"),
                ["RowNumber"] = ToInt(row, "RowNumber"),
                ["ReviewStatusCode"] = ToStringValue(row, "ReviewStatusCode"),
                ["ValidationStatusCode"] = ToStringValue(row, "ValidationStatusCode"),
                ["ValidationMessage"] = ToStringValue(row, "ValidationMessage")
            };

            var values = ParseRowDataJson(ToStringValue(row, "RowDataJson"));
            foreach (var col in dataColumns)
            {
                var fieldCode = col["FieldCode"];
                flattened[fieldCode] = values.TryGetValue(fieldCode, out var value) ? value : string.Empty;
            }

            records.Add(flattened);
        }

        if (!string.IsNullOrWhiteSpace(search) && dataColumns.Count > 0)
        {
            var term = search.Trim();
            records = records.Where(row => dataColumns.Any(col =>
            {
                var key = col["FieldCode"];
                return row.TryGetValue(key, out var value)
                    && Convert.ToString(value)?.Contains(term, StringComparison.OrdinalIgnoreCase) == true;
            })).ToList();
        }

        ViewBag.DataColumns = dataColumns;
        ViewBag.Records = records;

        //try
        //{
        //    var statusProcedureName = configs.AsEnumerable()
        //        .Where(r => ToInt(r, "GridType") == 2)
        //        .Select(r => ToStringValue(r, "StatusProcedureName"))
        //        .FirstOrDefault();

        //    if (string.IsNullOrWhiteSpace(statusProcedureName))
        //        statusProcedureName = "APS_InboxStatus_Default";

        //    if (!IsValidStatusProcedure(statusProcedureName))
        //        throw new InvalidOperationException("Configured status procedure name is invalid.");

        //    var statusRows = Db.Query(statusProcedureName, p =>
        //    {
        //        ApsDb.In(p, "@UseCaseID", useCaseId);
        //        ApsDb.In(p, "@UserName", ActionBy);
        //    });

        //    ViewBag.StatusRows = statusRows;
        //    ViewBag.StatusColumns = statusRows.Columns.Cast<DataColumn>()
        //        .Select(c => c.ColumnName)
        //        .ToList();
        //}
        //catch (Exception ex)
        //{
        //    ViewBag.StatusError = ex.Message;
        //    ViewBag.StatusRows = new DataTable();
        //    ViewBag.StatusColumns = new List<string>();
        //}

        return View();
    }

    [HttpGet]
    public IActionResult Detail(int id, byte mode = 0)
    {
        var record = Db.QuerySingle("APS_InflowData_GetById", p => ApsDb.In(p, "@InflowDataID", id));
        if (record == null)
            return RedirectToAction(nameof(Index));

        var useCaseId = ToInt(record, "UseCaseID");
        if (useCaseId <= 0 || !IsAllowedUseCase(useCaseId))
            return RedirectToAction("Denied", "Home", new { reason = "Forbidden" });

        var values = ParseRowDataJson(ToStringValue(record, "RowDataJson"));
        var layout = _detail.LoadLayout(useCaseId);

        ViewBag.Record = record;
        ViewBag.InflowDataId = id;
        ViewBag.Modes = layout.Modes;

        if (layout.Modes.Count > 0)
        {
            if (!layout.Modes.Contains(mode))
                mode = layout.Modes.Contains((byte)1) ? (byte)1 : layout.Modes[0];

            ViewBag.Mode = mode;
            ViewBag.Tiles = _detail.BuildTiles(layout.Tiles.Where(t => t.Mode == mode), id, values,
                                   useCaseId, layout.TileSourceSpecJson);
            ViewBag.Tabs = layout.Tabs.Where(t => t.Mode == mode).ToList();
            return View();
        }

        // No layout configured yet: show the template fields as before.
        var configSets = Db.QueryMultiple("APS_InflowConfig_GetActive", p => ApsDb.In(p, "@UseCaseID", useCaseId));
        var templateColumns = configSets.Tables.Count > 1 ? configSets.Tables[1] : new DataTable();

        var detailColumns = templateColumns.AsEnumerable()
            .Where(r => ToBool(r, "ShowInDetail"))
            .OrderBy(r => ToInt(r, "SortOrder"))
            .ToList();

        if (detailColumns.Count == 0)
            detailColumns = templateColumns.AsEnumerable().OrderBy(r => ToInt(r, "SortOrder")).ToList();

        ViewBag.Fields = detailColumns.Select(r => new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["FieldCode"] = ToStringValue(r, "FieldCode"),
            ["DisplayName"] = ToStringValue(r, "ExcelColumnName")
        }).ToList();
        ViewBag.Values = values;

        return View();
    }

    /// <summary>Data for one tab, loaded the first time the reviewer opens it.</summary>
    [HttpGet]
    public IActionResult TabData(int id, int tabId)
    {
        var context = LoadDetailContext(id, tabId, out var error);
        if (context == null) return Fail(error);

        try
        {
            return Ok(null, _detail.BuildTab(context.Value.Tab, id, context.Value.Values, context.Value.UseCaseId));
        }
        catch (InvalidOperationException ex)
        {
            return Fail(ex.Message);    
        }
        catch (Exception ex) when (ex is SqlException or JsonException)
        {
            _logger.LogError(ex, "Detail tab {TabId} failed for record {Id}.", tabId, id);
            return Fail("This tab could not be loaded. Check its configured source in Detail Setup.");
        }
    }

    /// <summary>
    /// Runs a published pipeline for this record, so a reviewer can pull fresh
    /// data without leaving the screen. Only a pipeline of the record's own use
    /// case may be started, and only the one the tab was configured with.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> RefreshTab(int id, int tabId)
    {
        var context = LoadDetailContext(id, tabId, out var error);
        if (context == null) return Fail(error);

        var tab = context.Value.Tab;
        if (tab.DisplayType != "NOTES") return Fail("This tab has nothing to refresh.");

        int pipelineId;
        try
        {
            using var settings = JsonDocument.Parse(tab.SettingsJson);
            pipelineId = settings.RootElement.TryGetProperty("refreshPipelineId", out var v) && v.TryGetInt32(out var p) ? p : 0;
        }
        catch (JsonException) { return Fail("This tab's settings could not be read."); }

        if (pipelineId <= 0) return Fail("No pipeline is configured for this tab.");

        var pipeline = Db.Query("API_Config_Pipeline_GetForUseCase", p => ApsDb.In(p, "@UseCaseID", context.Value.UseCaseId))
            .AsEnumerable()
            .FirstOrDefault(r => Convert.ToInt32(r["PipelineId"]) == pipelineId);

        if (pipeline == null) return Fail("That pipeline is not available for this use case.");

        var outputs = Db.ExecuteWithOutput("API_Config_Pipeline_StartRun", p =>
        {
            ApsDb.In(p, "@PipelineId", pipelineId);
            ApsDb.In(p, "@TriggerType", "MANUAL");
            ApsDb.In(p, "@RunBy", ActionBy);
            ApsDb.In(p, "@LeaseSeconds", 600);
            ApsDb.Out(p, "@PipelineRunId", SqlDbType.Int);
            ApsDb.Out(p, "@Message", SqlDbType.VarChar, 200);
        });

        var runId = outputs.TryGetValue("PipelineRunId", out var rid) && rid != null ? Convert.ToInt32(rid) : 0;
        if (runId == 0) return Fail(Convert.ToString(outputs["Message"]));

        // Only this record, so a reviewer's refresh never processes the queue.
        var summary = await _pipeline.RunAsync(runId, pipelineId, false, ActionBy, id, HttpContext.RequestAborted);

        return summary.Status == "Failed"
            ? Fail(summary.Message ?? "The refresh did not complete.")
            : Ok($"Refreshed: {summary.RowsInserted} new row(s).");
    }

    /// <summary>
    /// Streams a document. The browser only ever sends a token; the path is looked
    /// up again here, for this record, and must sit under Aps:DocumentRoots.
    /// </summary>
    [HttpGet]
    public IActionResult Document(int id, int tabId, string doc, int page = 1, bool download = false)
    {
        var file = ResolveDocumentFor(id, tabId, doc, out var error);
        if (file == null) return StatusCode(StatusCodes.Status404NotFound, error);

        Response.Headers["Cache-Control"] = "private, no-store";

        if (download)
            return PhysicalFile(file.FullPath, ApsDetailRuntime.ContentTypeFor(file.Extension), file.Name);

        switch (ApsDetailRuntime.PreviewKind(file.Extension))
        {
            case "pdf":
            case "image":
                Response.Headers["Content-Disposition"] =
                    new Microsoft.Net.Http.Headers.ContentDispositionHeaderValue("inline") { FileNameStar = file.Name }.ToString();
                return PhysicalFile(file.FullPath, ApsDetailRuntime.ContentTypeFor(file.Extension), enableRangeProcessing: true);

            case "tiff":
                // The browser renders TIFF pages, so the whole file is served once.
                return PhysicalFile(file.FullPath, "image/tiff", enableRangeProcessing: true);

            default:
                return StatusCode(StatusCodes.Status415UnsupportedMediaType, "This file type cannot be previewed. Use Download.");
        }
    }
    /// <summary>Saves one edited field value from the document viewer.</summary>
    [HttpPost]
    public IActionResult UpdateDocumentField(int id, int tabId, string rowKey, string value)
    {
        var context = LoadDetailContext(id, tabId, out var error);
        if (context == null) return Fail(error);

        if (value?.Length > 4000) return Fail("That value is longer than 4000 characters.");

        var result = _detail.UpdateField(context.Value.Tab, id, rowKey, value, ActionBy);
        return result.Ok ? Ok(result.Message) : Fail(result.Message);
    }
    /// <summary>Page count for a TIFF, so the viewer can offer page navigation.</summary>
    [HttpGet]
    public IActionResult DocumentPages(int id, int tabId, string doc)
    {
        var file = ResolveDocumentFor(id, tabId, doc, out var error);
        if (file == null) return Fail(error);

        if (ApsDetailRuntime.PreviewKind(file.Extension) != "tiff")
            return Ok(null, new { pages = 1 });

        if (!OperatingSystem.IsWindows())
            return Fail("TIFF preview needs a Windows server.");

        return Ok(null, new { pages = ApsDetailRuntime.CountTiffPages(file.FullPath) });
    }

    private ApsDetailRuntime.ResolvedDocument ResolveDocumentFor(int id, int tabId, string doc, out string error)
    {
        var context = LoadDetailContext(id, tabId, out error);
        if (context == null) return null;

        try
        {
            var file = _detail.ResolveDocument(context.Value.Tab, id, doc);
            error = file.Error;
            return file.Error == null ? file : null;
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException or JsonException)
        {
            _logger.LogError(ex, "Document lookup failed for record {Id}, tab {TabId}.", id, tabId);
            error = "The document could not be looked up.";
            return null;
        }
    }

    /// <summary>
    /// The record must exist, the caller must have its use case, and the tab
    /// must belong to that use case's layout.
    /// </summary>
    private (ApsDetailRuntime.Tab Tab, Dictionary<string, string> Values, int UseCaseId)? LoadDetailContext(int id, int tabId, out string error)
    {
        error = null;

        var record = Db.QuerySingle("APS_InflowData_GetById", p => ApsDb.In(p, "@InflowDataID", id));
        if (record == null) { error = "Record not found."; return null; }

        var useCaseId = ToInt(record, "UseCaseID");
        if (useCaseId <= 0 || !IsAllowedUseCase(useCaseId))
        {
            error = "You do not have access to this record.";
            return null;
        }

        var tab = _detail.LoadLayout(useCaseId).Tabs.FirstOrDefault(t => t.Id == tabId);
        if (tab == null) { error = "This tab is no longer configured."; return null; }

        return (tab, ParseRowDataJson(ToStringValue(record, "RowDataJson")), useCaseId);
    }

    [HttpPost]
    public IActionResult SaveReview(int inflowDataId, string reviewStatusCode, string reviewComment)
    {
        var target = Db.QuerySingle("APS_InflowData_GetById", p => ApsDb.In(p, "@InflowDataID", inflowDataId));
        if (target == null || !IsAllowedUseCase(ToInt(target, "UseCaseID")))
            return Fail("You do not have access to this record.");
        var outputs = Db.ExecuteWithOutput("APS_InflowData_SaveReview", p =>
        {
            ApsDb.In(p, "@InflowDataID", inflowDataId);
            ApsDb.In(p, "@ReviewStatusCode", reviewStatusCode);
            ApsDb.In(p, "@ReviewComment", string.IsNullOrWhiteSpace(reviewComment) ? null : reviewComment.Trim());
            ApsDb.In(p, "@ActionBy", ActionBy);
            ApsDb.Out(p, "@Success", SqlDbType.Bit);
            ApsDb.Out(p, "@Message", SqlDbType.NVarChar, 500);
        });

        return FromProcedure(outputs);
    }

    private DataTable LoadUseCases()
    {
        var userName = Identity.AccessType == ApsIdentity.RoleAdmin ? null : ActionBy;
        return Db.Query("APS_UseCaseMaster_Get", p =>
        {
            ApsDb.In(p, "@ClientID", null);
            ApsDb.In(p, "@ProcessID", null);
            ApsDb.In(p, "@UserName", userName);
        });
    }

    private bool IsAllowedUseCase(int useCaseId)
    {
        if (Identity.AccessType == ApsIdentity.RoleAdmin)
            return true;

        var outputs = Db.ExecuteWithOutput("APS_UserUseCaseMapping_Check", p =>
        {
            ApsDb.In(p, "@UserName", ActionBy);
            ApsDb.In(p, "@UseCaseID", useCaseId);
            ApsDb.Out(p, "@IsAllowed", SqlDbType.Bit);
        });

        return outputs.TryGetValue("IsAllowed", out var value)
            && value != null
            && Convert.ToBoolean(value);
    }

    private static Dictionary<string, string> ParseRowDataJson(string rowDataJson)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(rowDataJson))
            return result;

        try
        {
            using var document = JsonDocument.Parse(rowDataJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return result;

            foreach (var property in document.RootElement.EnumerateObject())
            {
                result[property.Name] = property.Value.ValueKind == JsonValueKind.String
                    ? property.Value.GetString() ?? string.Empty
                    : property.Value.ToString();
            }
        }
        catch
        {
            return result;
        }

        return result;
    }

    private static bool IsValidStatusProcedure(string procedureName)
    {
        return Regex.IsMatch(procedureName, "^[A-Za-z][A-Za-z0-9_]*$")
            && procedureName.StartsWith("APS_", StringComparison.Ordinal);
    }

    private static Dictionary<string, string> NewColumn(string fieldCode, string displayName) =>
    new(StringComparer.OrdinalIgnoreCase)
    {
        ["FieldCode"] = fieldCode,
        ["DisplayName"] = string.IsNullOrWhiteSpace(displayName) ? fieldCode : displayName
    };
    private static string ToStringValue(DataRow row, string column)
    {
        var actual = FindColumn(row.Table, column);
        if (actual == null) return string.Empty;
        return row[actual] == DBNull.Value ? string.Empty : Convert.ToString(row[actual]) ?? string.Empty;
    }

    private static int ToInt(DataRow row, string column)
    {
        var actual = FindColumn(row.Table, column);
        if (actual == null || row[actual] == DBNull.Value) return 0;
        return int.TryParse(Convert.ToString(row[actual]), out var value) ? value : 0;
    }

    private static bool ToBool(DataRow row, string column)
    {
        var actual = FindColumn(row.Table, column);
        if (actual == null || row[actual] == DBNull.Value) return false;
        return Convert.ToBoolean(row[actual]);
    }

    private static string FindColumn(DataTable table, string name)
    {
        return table.Columns.Cast<DataColumn>()
            .Select(c => c.ColumnName)
            .FirstOrDefault(c => string.Equals(c, name, StringComparison.OrdinalIgnoreCase));
    }
}

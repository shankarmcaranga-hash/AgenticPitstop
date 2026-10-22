using System.Data;
using System.Text;
using System.Text.Json;
using AgenticPitstop.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace AgenticPitstop.Controllers;

/// <summary>
/// The API Configurator, delivered as one controller and one view.
///
/// Seven screens live behind a single Index action driven by ?tab=. Every
/// action that changes data or reaches a network revalidates on the server -
/// the browser is never trusted, including for the wizard's own field rules.
///
/// Add [ApsAuthorize] below once you know which role should own this screen.
/// It is left off here only because this file must not assume an attribute
/// signature it cannot see.
/// </summary>
// [ApsAuthorize]
public sealed class ApiConfigController : ApsController
{
    private readonly ApiConfigDataAccess _api;
    private readonly ApiConfigSecretResolver _secrets;
    private readonly IConfiguration _config;

    public ApiConfigController(ApsDb db, ApsIdentity identity, ApsMetadata meta,
                               ApiConfigDataAccess api, ApiConfigSecretResolver secrets, IConfiguration config)
        : base(db, identity, meta)
    {
        _api = api;
        _secrets = secrets;
        _config = config;
    }

    protected override string ActiveScreenKey => "api-configurator";

    private const string ViewName = "ApiConfig";

    private bool SimulatorEnabled => _config.GetValue("ApiConfigurator:EnableSimulator", true);
    private bool LoadSampleEnabled => _config.GetValue("ApiConfigurator:EnableLoadSample", true);
    private bool AllowNonHttps => _config.GetValue("ApiConfigurator:AllowNonHttpsTargets", false);

    // =========================================================================
    // ONE ENTRY POINT
    // =========================================================================
    [HttpGet]
    public IActionResult Index(string tab, int? id, int? executionId, int? step,
                               string search, string status, DateTime? from, DateTime? to)
    {
        tab = string.IsNullOrWhiteSpace(tab) ? "dashboard" : tab.ToLowerInvariant();

        if (tab == "simulator" && !SimulatorEnabled)
        {
            Warn("The simulator is disabled in this environment.");
            tab = "dashboard";
        }

        ViewBag.Tab = tab;
        ViewBag.SimulatorEnabled = SimulatorEnabled;
        ViewBag.LoadSampleEnabled = LoadSampleEnabled;
        ViewBag.Step = step is >= 1 and <= 6 ? step.Value : 1;
        ViewBag.Search = search;
        ViewBag.Status = status;
        ViewBag.From = from;
        ViewBag.To = to;

        switch (tab)
        {
            case "catalog": LoadCatalog(search, status); break;
            case "configure": LoadConfigure(id); break;
            case "simulator": LoadSimulator(executionId); break;
            case "approvals": LoadApprovals(id); break;
            case "audit": LoadAudit(id, search, status, from, to); break;
            case "runtime": LoadRuntime(); break;
            default: LoadDashboard(); break;
        }

        return View(ViewName);
    }

    // =========================================================================
    // TAB LOADERS
    // =========================================================================
    private void LoadDashboard()
    {
        try
        {
            var data = _api.DataSet("dbo.API_Config_Dashboard_GetMetrics", ApiConfigDataAccess.P("@ReportingDays", 30));
            ViewBag.Metrics = ApiConfigDataAccess.First(data.Tables[0]);
            ViewBag.Recent = data.Tables.Count > 1 ? data.Tables[1] : new DataTable();
            ViewBag.MetricsAvailable = ViewBag.Metrics != null;
        }
        catch (Exception ex)
        {
            // Never fabricate a metric. Show that it is unavailable instead.
            ViewBag.MetricsAvailable = false;
            ViewBag.MetricsError = ex.Message;
            ViewBag.Recent = new DataTable();
        }
    }

    private void LoadCatalog(string search, string status)
    {
        ViewBag.Connectors = _api.Table("dbo.API_Config_Connector_GetCatalog",
            ApiConfigDataAccess.P("@Search", (object)search ?? DBNull.Value),
            ApiConfigDataAccess.P("@CustomerId", DBNull.Value),
            ApiConfigDataAccess.P("@Status", (object)status ?? DBNull.Value));
    }

    private void LoadConfigure(int? id)
    {
        ViewBag.Customers = _api.Table("dbo.API_Config_Customer_GetList");
        ViewBag.Applications = _api.Table("dbo.API_Config_Application_GetList", ApiConfigDataAccess.P("@CustomerId", DBNull.Value));
        ViewBag.Providers = _api.Table("dbo.API_Config_Provider_GetList");

        ViewBag.CheckResults = TempData["CheckResults"] as string;
        ViewBag.CheckType = TempData["CheckType"] as string;

        if (id is not > 0)
        {
            ViewBag.ReadOnly = false;
            return;
        }

        var data = _api.DataSet("dbo.API_Config_ConnectorVersion_Get", ApiConfigDataAccess.P("@ConnectorVersionId", id.Value));
        var version = ApiConfigDataAccess.First(data.Tables[0]);

        if (version == null)
        {
            Fail("That connector version no longer exists.");
            ViewBag.ReadOnly = false;
            return;
        }

        ViewBag.Version = version;
        ViewBag.Headers = data.Tables[1];
        ViewBag.QueryParams = data.Tables[2];
        ViewBag.AllowedHosts = data.Tables[3];
        ViewBag.SuccessCodes = data.Tables[4];
        ViewBag.Auth = ApiConfigDataAccess.First(data.Tables[5]);
        ViewBag.CheckRuns = data.Tables.Count > 6 ? data.Tables[6] : new DataTable();
        ViewBag.ReadOnly = ApiConfigDataAccess.Str(version, "Status") == "Published";

        ViewBag.SuccessCodesCsv = string.Join(",", data.Tables[4].Rows.Cast<DataRow>()
            .Select(r => ApiConfigDataAccess.Int(r, "StatusCode")));

        ViewBag.AllowlistText = string.Join("\n", data.Tables[3].Rows.Cast<DataRow>()
            .Select(r => ApiConfigDataAccess.Int(r, "PortNumber") > 0
                ? $"{ApiConfigDataAccess.Str(r, "HostName")}:{ApiConfigDataAccess.Int(r, "PortNumber")}"
                : ApiConfigDataAccess.Str(r, "HostName")));
    }

    private void LoadSimulator(int? executionId)
    {
        ViewBag.Connectors = _api.Table("dbo.API_Config_Connector_GetPublishedList");

        if (executionId is not > 0) return;

        var data = _api.DataSet("dbo.API_Config_Execution_GetById", ApiConfigDataAccess.P("@ExecutionId", executionId.Value));
        var request = ApiConfigDataAccess.First(data.Tables[0]);
        if (request == null) return;

        ViewBag.Request = request;
        ViewBag.Attempts = data.Tables[1];
        ViewBag.PrettyPayload = ApiConfigPayloadResolver.Pretty(ApiConfigDataAccess.Str(request, "ResolvedPayloadJson"));
    }

    private void LoadApprovals(int? id)
    {
        if (id is not > 0)
        {
            ViewBag.Pending = _api.Table("dbo.API_Config_Execution_GetPending");
            return;
        }

        var data = _api.DataSet("dbo.API_Config_Execution_GetById", ApiConfigDataAccess.P("@ExecutionId", id.Value));
        var request = ApiConfigDataAccess.First(data.Tables[0]);

        if (request == null)
        {
            Fail("That execution no longer exists.");
            ViewBag.Pending = _api.Table("dbo.API_Config_Execution_GetPending");
            return;
        }

        ViewBag.Request = request;
        ViewBag.Attempts = data.Tables[1];
        ViewBag.Headers = data.Tables[2];
        ViewBag.SuccessCodes = data.Tables[3];
        ViewBag.Exceptions = data.Tables[4];
        ViewBag.PrettyPayload = ApiConfigPayloadResolver.Pretty(ApiConfigDataAccess.Str(request, "ResolvedPayloadJson"));

        // Show the approver whether the stored package still matches its hash.
        var recomputed = ApiConfigExecutionEngine.ComputeApprovalHash(
            ApiConfigDataAccess.Str(request, "ExecutionId"),
            ApiConfigDataAccess.Str(request, "ResolvedMethod"),
            ApiConfigDataAccess.Str(request, "ResolvedUrl"),
            ApiConfigDataAccess.Str(request, "ResolvedPayloadJson"),
            ApiConfigDataAccess.Str(request, "ConfigHash"));

        var stored = ApiConfigDataAccess.Str(request, "ApprovalHash");
        ViewBag.HashVerified = string.IsNullOrEmpty(stored)
            ? (bool?)null
            : string.Equals(stored, recomputed, StringComparison.OrdinalIgnoreCase);
    }

    private void LoadAudit(int? id, string search, string status, DateTime? from, DateTime? to)
    {
        if (id is > 0)
        {
            var data = _api.DataSet("dbo.API_Config_Execution_GetById", ApiConfigDataAccess.P("@ExecutionId", id.Value));
            var request = ApiConfigDataAccess.First(data.Tables[0]);

            if (request != null)
            {
                ViewBag.Request = request;
                ViewBag.Attempts = data.Tables[1];
                ViewBag.Exceptions = data.Tables[4];
                ViewBag.PrettyPayload = ApiConfigPayloadResolver.Pretty(ApiConfigDataAccess.Str(request, "ResolvedPayloadJson"));
                return;
            }

            Fail("That execution no longer exists.");
        }

        ViewBag.Executions = AuditQuery(search, status, from, to);
        ViewBag.AuditEvents = _api.Table("dbo.API_Config_Audit_GetRecent", ApiConfigDataAccess.P("@Top", 40));
    }

    private void LoadRuntime()
    {
        ViewBag.Providers = _api.Table("dbo.API_Config_Provider_GetList");
        ViewBag.AwsEnabled = _config.GetValue("Aws:Enabled", false);
        ViewBag.Region = _config["Aws:Region"];
        ViewBag.SecretAdapter = _secrets.ActiveAdapter;
        ViewBag.TransportAdapter = _config.GetValue("Aws:Enabled", false) && _config.GetValue("Aws:Sqs:Enabled", false)
            ? "AwsSqs" : "SqlOutboxDirect";
    }

    private DataTable AuditQuery(string search, string status, DateTime? from, DateTime? to) =>
        _api.Table("dbo.API_Config_Execution_GetAudit",
            ApiConfigDataAccess.P("@FromDate", (object)from ?? DBNull.Value),
            ApiConfigDataAccess.P("@ToDate", (object)to ?? DBNull.Value),
            ApiConfigDataAccess.P("@Status", (object)status ?? DBNull.Value),
            ApiConfigDataAccess.P("@Search", (object)search ?? DBNull.Value),
            ApiConfigDataAccess.P("@Top", 200));

    // =========================================================================
    // CATALOG ACTIONS
    // =========================================================================
    [HttpPost]
    public IActionResult Clone(int id)
    {
        try
        {
            var newVersion = ApiConfigDataAccess.Out("@NewVersionId", SqlDbType.Int);
            var result = _api.Table("dbo.API_Config_ConnectorVersion_Clone",
                ApiConfigDataAccess.P("@SourceVersionId", id),
                ApiConfigDataAccess.P("@ActorName", ActionBy),
                newVersion);

            var row = ApiConfigDataAccess.First(result);
            Success("A new draft version was created from the published version.");
            return RedirectToAction(nameof(Index), new { tab = "configure", id = ApiConfigDataAccess.Int(row, "NewVersionId") });
        }
        catch (Exception ex)
        {
            Fail(ex.Message);
            return RedirectToAction(nameof(Index), new { tab = "catalog" });
        }
    }

    [HttpPost]
    public IActionResult SetActive(int id, bool enabled)
    {
        try
        {
            _api.Execute("dbo.API_Config_Connector_SetActive",
                ApiConfigDataAccess.P("@ConnectorId", id),
                ApiConfigDataAccess.P("@Enabled", enabled),
                ApiConfigDataAccess.P("@ActorName", ActionBy));

            Success(enabled ? "Connector reactivated." : "Connector deactivated. No new executions can be created.");
        }
        catch (Exception ex)
        {
            Fail(ex.Message);
        }

        return RedirectToAction(nameof(Index), new { tab = "catalog" });
    }

    // =========================================================================
    // WIZARD ACTIONS
    // =========================================================================
    [HttpPost]
    public IActionResult SaveDraft()
    {
        var versionId = ParseId(Form("cfgVersionId"));

        try
        {
            var newVersionId = ApiConfigDataAccess.Out("@NewConnectorVersionId", SqlDbType.Int);
            var newConnectorId = ApiConfigDataAccess.Out("@NewConnectorId", SqlDbType.Int);
            var versionNumber = ApiConfigDataAccess.Out("@VersionNumberOut", SqlDbType.Int);

            var result = _api.Table("dbo.API_Config_ConnectorVersion_SaveDraft",
                ApiConfigDataAccess.P("@ConnectorVersionId", (object)versionId ?? DBNull.Value),
                ApiConfigDataAccess.P("@CustomerId", int.Parse(Form("cfgCustomer"))),
                ApiConfigDataAccess.P("@ApplicationId", int.Parse(Form("cfgApplication"))),
                ApiConfigDataAccess.P("@ConnectorCode", Form("cfgCode")),
                ApiConfigDataAccess.P("@ConnectorName", Form("cfgName")),
                ApiConfigDataAccess.P("@OperationCode", Form("cfgOperation")),
                ApiConfigDataAccess.P("@Protocol", Form("cfgProtocol")),
                ApiConfigDataAccess.P("@ExecutionModeAllowed", Form("cfgMode")),
                ApiConfigDataAccess.P("@HttpMethod", (object)FormOrNull("cfgMethod") ?? DBNull.Value),
                ApiConfigDataAccess.P("@BaseUrlAlias", (object)FormOrNull("cfgBaseAlias") ?? DBNull.Value),
                ApiConfigDataAccess.P("@BaseUrl", (object)FormOrNull("cfgBaseUrl") ?? DBNull.Value),
                ApiConfigDataAccess.P("@EndpointTemplate", (object)FormOrNull("cfgEndpoint") ?? DBNull.Value),
                ApiConfigDataAccess.P("@SampleSourceJson", (object)FormOrNull("cfgSourceJson") ?? DBNull.Value),
                ApiConfigDataAccess.P("@PayloadTemplateJson", (object)FormOrNull("cfgPayloadTemplate") ?? DBNull.Value),
                ApiConfigDataAccess.P("@TimeoutSeconds", FormInt("cfgTimeout", 30)),
                ApiConfigDataAccess.P("@MaxAttempts", FormInt("cfgRetries", 3)),
                ApiConfigDataAccess.P("@IdempotencyHeader", (object)FormOrNull("cfgIdempotency") ?? DBNull.Value),
                ApiConfigDataAccess.P("@CorrelationHeader", (object)FormOrNull("cfgCorrelation") ?? DBNull.Value),
                ApiConfigDataAccess.P("@ResponseType", Form("cfgResponseType")),
                ApiConfigDataAccess.P("@ResultMappingJson", (object)FormOrNull("cfgResponseMapping") ?? DBNull.Value),
                ApiConfigDataAccess.P("@IsRetrySafe", FormBool("cfgRetrySafe")),
                ApiConfigDataAccess.P("@AuthJson", BuildAuthJson()),
                ApiConfigDataAccess.P("@HeadersJson", BuildPairJson("headerName", "headerValue")),
                ApiConfigDataAccess.P("@QueryParamsJson", BuildPairJson("queryName", "queryValue")),
                ApiConfigDataAccess.P("@AllowedHostsJson", BuildHostJson()),
                ApiConfigDataAccess.P("@SuccessCodesCsv", (object)FormOrNull("cfgSuccessCodes") ?? DBNull.Value),
                ApiConfigDataAccess.P("@RowVersionHex", (object)FormOrNull("cfgRowVersion") ?? DBNull.Value),
                ApiConfigDataAccess.P("@ActorName", ActionBy),
                newVersionId, newConnectorId, versionNumber);

            var row = ApiConfigDataAccess.First(result);
            var savedId = row != null ? ApiConfigDataAccess.Int(row, "ConnectorVersionId") : versionId ?? 0;

            Success($"Draft saved as version {ApiConfigDataAccess.Int(row, "VersionNumber", 1)}.");
            return RedirectToAction(nameof(Index), new { tab = "configure", id = savedId, step = FormInt("cfgCurrentStep", 1) });
        }
        catch (Exception ex)
        {
            Fail(ex.Message);
            return RedirectToAction(nameof(Index), new { tab = "configure", id = versionId });
        }
    }

    /// <summary>Resolve Preview. No target call and no database write.</summary>
    [HttpPost]
    public IActionResult ResolvePreview()
    {
        var template = Form("payloadTemplate");
        var source = Form("sourceJson");

        if (!ApiConfigPayloadResolver.IsValidJson(source))
            return Json(new { ok = false, message = "The sample source data is not valid JSON." });

        if (!ApiConfigPayloadResolver.IsValidJson(template))
            return Json(new { ok = false, message = "The payload template is not valid JSON." });

        var resolved = ApiConfigPayloadResolver.Resolve(template, source, Guid.NewGuid().ToString(), out var unresolved);

        return Json(new
        {
            ok = unresolved.Count == 0,
            resolved = ApiConfigPayloadResolver.Pretty(resolved),
            unresolved,
            message = unresolved.Count == 0
                ? "All placeholders resolved."
                : $"{unresolved.Count} placeholder(s) did not resolve: {string.Join(", ", unresolved)}"
        });
    }

    [HttpPost]
    public IActionResult Validate(int id)
    {
        var data = _api.DataSet("dbo.API_Config_ConnectorVersion_Get", ApiConfigDataAccess.P("@ConnectorVersionId", id));
        var version = ApiConfigDataAccess.First(data.Tables[0]);

        if (version == null)
        {
            Fail("The connector version could not be loaded.");
            return RedirectToAction(nameof(Index), new { tab = "configure" });
        }

        var results = ApiConfigChecks.Validate(version, data.Tables[1], data.Tables[2], data.Tables[3],
                                               data.Tables[4], ApiConfigDataAccess.First(data.Tables[5]), AllowNonHttps);

        var status = ApiConfigChecks.HasErrors(results) ? "Failed" : "Passed";

        _api.Execute("dbo.API_Config_CheckRun_Insert",
            ApiConfigDataAccess.P("@ConnectorVersionId", id),
            ApiConfigDataAccess.P("@RunType", "Validation"),
            ApiConfigDataAccess.P("@ResultStatus", status),
            ApiConfigDataAccess.P("@ResultJson", ApiConfigChecks.ToJson(results)),
            ApiConfigDataAccess.P("@StartedOn", DBNull.Value),
            ApiConfigDataAccess.P("@RunBy", ActionBy));

        TempData["CheckResults"] = ApiConfigChecks.ToJson(results);
        TempData["CheckType"] = "Validation";

        if (status == "Passed") Success("Validation passed.");
        else Fail("Validation found errors that block publishing.");

        return RedirectToAction(nameof(Index), new { tab = "configure", id, step = 6 });
    }

    [HttpPost]
    public async Task<IActionResult> TestConnection(int id, CancellationToken cancellationToken)
    {
        var data = _api.DataSet("dbo.API_Config_ConnectorVersion_Get", ApiConfigDataAccess.P("@ConnectorVersionId", id));
        var version = ApiConfigDataAccess.First(data.Tables[0]);

        if (version == null)
        {
            Fail("The connector version could not be loaded.");
            return RedirectToAction(nameof(Index), new { tab = "configure" });
        }

        // A connection test is blocked while structural validation has errors.
        var validation = ApiConfigChecks.Validate(version, data.Tables[1], data.Tables[2], data.Tables[3],
                                                  data.Tables[4], ApiConfigDataAccess.First(data.Tables[5]), AllowNonHttps);

        if (ApiConfigChecks.HasErrors(validation))
        {
            TempData["CheckResults"] = ApiConfigChecks.ToJson(validation);
            TempData["CheckType"] = "Validation";
            Fail("Fix the validation errors before running a connection test.");
            return RedirectToAction(nameof(Index), new { tab = "configure", id, step = 6 });
        }

        var started = DateTime.Now;
        var steps = await ApiConfigChecks.TestConnectionAsync(version, data.Tables[3],
                                                              ApiConfigDataAccess.First(data.Tables[5]),
                                                              _secrets, cancellationToken);

        var passed = !steps.Rows.Cast<DataRow>().Any(r => ApiConfigDataAccess.Str(r, "Status") == "Failed");

        _api.Execute("dbo.API_Config_CheckRun_Insert",
            ApiConfigDataAccess.P("@ConnectorVersionId", id),
            ApiConfigDataAccess.P("@RunType", "ConnectionTest"),
            ApiConfigDataAccess.P("@ResultStatus", passed ? "Passed" : "Failed"),
            ApiConfigDataAccess.P("@ResultJson", ApiConfigChecks.ToJson(steps)),
            ApiConfigDataAccess.P("@StartedOn", started),
            ApiConfigDataAccess.P("@RunBy", ActionBy));

        TempData["CheckResults"] = ApiConfigChecks.ToJson(steps);
        TempData["CheckType"] = "ConnectionTest";

        if (passed) Success("Connection test passed. Publishing is still a separate, explicit step.");
        else Fail("Connection test failed. See the step results.");

        return RedirectToAction(nameof(Index), new { tab = "configure", id, step = 6 });
    }

    [HttpPost]
    public IActionResult Publish(int id)
    {
        var data = _api.DataSet("dbo.API_Config_ConnectorVersion_Get", ApiConfigDataAccess.P("@ConnectorVersionId", id));
        var version = ApiConfigDataAccess.First(data.Tables[0]);

        if (version == null)
        {
            Fail("The connector version could not be loaded.");
            return RedirectToAction(nameof(Index), new { tab = "configure" });
        }

        var results = ApiConfigChecks.Validate(version, data.Tables[1], data.Tables[2], data.Tables[3],
                                               data.Tables[4], ApiConfigDataAccess.First(data.Tables[5]), AllowNonHttps);

        if (ApiConfigChecks.HasErrors(results))
        {
            TempData["CheckResults"] = ApiConfigChecks.ToJson(results);
            TempData["CheckType"] = "Validation";
            Fail("Publishing is blocked until the validation errors are fixed.");
            return RedirectToAction(nameof(Index), new { tab = "configure", id, step = 6 });
        }

        try
        {
            var published = _api.Table("dbo.API_Config_ConnectorVersion_Publish",
                ApiConfigDataAccess.P("@ConnectorVersionId", id),
                ApiConfigDataAccess.P("@ActorName", ActionBy),
                ApiConfigDataAccess.P("@RequireConnectionTest",
                    _config.GetValue("ApiConfigurator:RequireConnectionTestBeforePublish", false)));

            var row = ApiConfigDataAccess.First(published);
            Success($"Version published and frozen. Config hash {ApiConfigDataAccess.ShortHash(ApiConfigDataAccess.Str(row, "ConfigHash"))}.");
            return RedirectToAction(nameof(Index), new { tab = "configure", id });
        }
        catch (Exception ex)
        {
            Fail(ex.Message);
            return RedirectToAction(nameof(Index), new { tab = "configure", id, step = 6 });
        }
    }

    // =========================================================================
    // SIMULATOR ACTIONS
    // =========================================================================
    [HttpPost]
    public IActionResult CreateReview()
    {
        if (!SimulatorEnabled)
        {
            Fail("The simulator is disabled in this environment.");
            return RedirectToAction(nameof(Index));
        }

        var versionId = int.Parse(Form("simConnector"));
        var sourceJson = Form("simSource");

        if (!ApiConfigPayloadResolver.IsValidJson(sourceJson))
        {
            Fail("The source data is not valid JSON.");
            return RedirectToAction(nameof(Index), new { tab = "simulator" });
        }

        var data = _api.DataSet("dbo.API_Config_ConnectorVersion_Get", ApiConfigDataAccess.P("@ConnectorVersionId", versionId));
        var version = ApiConfigDataAccess.First(data.Tables[0]);

        if (version == null)
        {
            Fail("The connector version could not be loaded.");
            return RedirectToAction(nameof(Index), new { tab = "simulator" });
        }

        // The external handle is generated first so {{System.ExecutionId}} resolves
        // to the same value the executor will later send. The INT ExecutionId is
        // assigned by IDENTITY and does not exist yet.
        var executionUid = Guid.NewGuid();

        var url = ApiConfigPayloadResolver.BuildUrl(
            ApiConfigDataAccess.Str(version, "BaseUrl"),
            ApiConfigDataAccess.Str(version, "EndpointTemplate"),
            data.Tables[2], sourceJson, executionUid.ToString(), out var urlUnresolved);

        var payload = ApiConfigPayloadResolver.Resolve(
            ApiConfigDataAccess.Str(version, "PayloadTemplateJson"),
            sourceJson, executionUid.ToString(), out var payloadUnresolved);

        var unresolved = urlUnresolved.Concat(payloadUnresolved).Distinct().ToList();
        if (unresolved.Count > 0)
        {
            Fail($"Cannot create a review: {unresolved.Count} placeholder(s) did not resolve ({string.Join(", ", unresolved)}).");
            return RedirectToAction(nameof(Index), new { tab = "simulator" });
        }

        try
        {
            var createdId = ApiConfigDataAccess.Out("@ExecutionId", SqlDbType.Int);

            var result = _api.Table("dbo.API_Config_Execution_CreateReview",
                ApiConfigDataAccess.P("@ConnectorVersionId", versionId),
                ApiConfigDataAccess.P("@ExecutionUid", executionUid),
                ApiConfigDataAccess.P("@BusinessKey", (object)FormOrNull("simBusinessKey") ?? DBNull.Value),
                ApiConfigDataAccess.P("@ExecutionMode", Form("simMode")),
                ApiConfigDataAccess.P("@ResolvedMethod", ApiConfigDataAccess.Str(version, "HttpMethod")),
                ApiConfigDataAccess.P("@ResolvedUrl", url),
                ApiConfigDataAccess.P("@ResolvedPayloadJson", payload),
                ApiConfigDataAccess.P("@SourceDataJson", sourceJson),
                ApiConfigDataAccess.P("@ActorName", ActionBy),
                createdId);

            var row = ApiConfigDataAccess.First(result);
            Success("Review created and waiting in the approval inbox.");
            return RedirectToAction(nameof(Index), new { tab = "simulator", executionId = ApiConfigDataAccess.Int(row, "ExecutionId") });
        }
        catch (Exception ex)
        {
            Fail(ex.Message);
            return RedirectToAction(nameof(Index), new { tab = "simulator" });
        }
    }

    /// <summary>
    /// Alters the stored package without touching the approval hash, so the next
    /// execute attempt must be blocked. Proves the control actually fires.
    /// </summary>
    [HttpPost]
    public IActionResult Tamper(int id)
    {
        if (!SimulatorEnabled)
        {
            Fail("The simulator is disabled in this environment.");
            return RedirectToAction(nameof(Index));
        }

        _api.Execute("dbo.API_Config_Execution_SimulateTamper", ApiConfigDataAccess.P("@ExecutionId", id));
        Warn("The stored package was altered after approval. The next execute attempt should now be blocked.");

        return RedirectToAction(nameof(Index), new { tab = "simulator", executionId = id });
    }

    // =========================================================================
    // APPROVAL ACTIONS
    // =========================================================================
    [HttpPost]
    public IActionResult Approve(int id, string comment)
    {
        try
        {
            _api.Execute("dbo.API_Config_Execution_Approve",
                ApiConfigDataAccess.P("@ExecutionId", id),
                ApiConfigDataAccess.P("@ActorName", ActionBy),
                ApiConfigDataAccess.P("@Comment", (object)comment ?? DBNull.Value));

            Success("Approved. The approval hash is now frozen over the exact package you reviewed.");
        }
        catch (Exception ex)
        {
            Fail(ex.Message);
        }

        return RedirectToAction(nameof(Index), new { tab = "approvals", id });
    }

    [HttpPost]
    public IActionResult Reject(int id, string comment)
    {
        try
        {
            _api.Execute("dbo.API_Config_Execution_Reject",
                ApiConfigDataAccess.P("@ExecutionId", id),
                ApiConfigDataAccess.P("@ActorName", ActionBy),
                ApiConfigDataAccess.P("@Comment", (object)comment ?? DBNull.Value));

            Success("Rejected. No outbox message was created, so nothing can reach the target.");
        }
        catch (Exception ex)
        {
            Fail(ex.Message);
        }

        return RedirectToAction(nameof(Index), new { tab = "approvals", id });
    }

    [HttpPost]
    public IActionResult Execute(int id)
    {
        try
        {
            _api.Execute("dbo.API_Config_Execution_Queue",
                ApiConfigDataAccess.P("@ExecutionId", id),
                ApiConfigDataAccess.P("@ActorName", ActionBy));

            Success("Queued. The worker will send the persisted approved package.");
        }
        catch (Exception ex)
        {
            Fail(ex.Message);
        }

        return RedirectToAction(nameof(Index), new { tab = "approvals", id });
    }

    // =========================================================================
    // AUDIT EXPORT
    // =========================================================================
    [HttpGet]
    public IActionResult Export(string search, string status, DateTime? from, DateTime? to)
    {
        var table = AuditQuery(search, status, from, to);

        var exported = table.Rows.Cast<DataRow>().Select(row => new
        {
            executionId = ApiConfigDataAccess.Int(row, "ExecutionId"),
            externalHandle = ApiConfigDataAccess.Str(row, "ExecutionUid"),
            businessKey = ApiConfigDataAccess.Str(row, "BusinessKey"),
            connector = ApiConfigDataAccess.Str(row, "ConnectorName"),
            connectorCode = ApiConfigDataAccess.Str(row, "ConnectorCode"),
            operation = ApiConfigDataAccess.Str(row, "OperationCode"),
            version = ApiConfigDataAccess.Int(row, "VersionNumber"),
            customer = ApiConfigDataAccess.Str(row, "CustomerName"),
            application = ApiConfigDataAccess.Str(row, "ApplicationName"),
            mode = ApiConfigDataAccess.Str(row, "ExecutionMode"),
            status = ApiConfigDataAccess.Str(row, "Status"),
            method = ApiConfigDataAccess.Str(row, "ResolvedMethod"),
            url = ApiConfigDataAccess.Str(row, "ResolvedUrl"),
            payloadHash = ApiConfigDataAccess.Str(row, "PayloadHash"),
            configHash = ApiConfigDataAccess.Str(row, "ConfigHash"),
            approvalStatus = ApiConfigDataAccess.Str(row, "ApprovalStatus"),
            approvalHash = ApiConfigDataAccess.Str(row, "ApprovalHash"),
            decidedBy = ApiConfigDataAccess.Str(row, "DecidedBy"),
            decidedOn = ApiConfigDataAccess.Date(row, "DecidedOn"),
            createdOn = ApiConfigDataAccess.Date(row, "CreatedOn"),
            completedOn = ApiConfigDataAccess.Date(row, "CompletedOn"),
            attemptCount = ApiConfigDataAccess.Int(row, "AttemptCount"),
            lastStatusCode = ApiConfigDataAccess.Int(row, "LastStatusCode")
        }).ToList();

        // Built on the server from SQL. No secret values, no unredacted bodies.
        var json = JsonSerializer.Serialize(new
        {
            exportedOn = DateTime.Now,
            exportedBy = ActionBy,
            filters = new { search, status, from, to },
            count = exported.Count,
            executions = exported
        }, new JsonSerializerOptions { WriteIndented = true });

        return File(Encoding.UTF8.GetBytes(json), "application/json",
                    $"api-config-audit-{DateTime.Now:yyyyMMdd-HHmmss}.json");
    }

    // =========================================================================
    // FORM HELPERS
    // =========================================================================
    private string Form(string key) => Request.Form.ContainsKey(key) ? Request.Form[key].ToString().Trim() : null;

    private string FormOrNull(string key)
    {
        var value = Form(key);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private int FormInt(string key, int fallback) => int.TryParse(Form(key), out var value) ? value : fallback;

    private bool FormBool(string key) => Form(key) is "true" or "on" or "1" or "True";

    private string[] FormArray(string key) => Request.Form.ContainsKey(key) ? Request.Form[key].ToArray() : Array.Empty<string>();

    private static int? ParseId(string value) => int.TryParse(value, out var parsed) && parsed > 0 ? parsed : null;

    /// <summary>Builds [{"name":"","value":"","sort":0}] from two parallel form arrays.</summary>
    private string BuildPairJson(string nameKey, string valueKey)
    {
        var names = FormArray(nameKey);
        var values = FormArray(valueKey);

        var rows = new List<Dictionary<string, object>>();
        for (var i = 0; i < names.Length; i++)
        {
            var name = names[i]?.Trim();
            if (string.IsNullOrWhiteSpace(name)) continue;

            rows.Add(new Dictionary<string, object>
            {
                ["name"] = name,
                ["value"] = i < values.Length ? values[i] : string.Empty,
                ["sort"] = i
            });
        }

        return JsonSerializer.Serialize(rows);
    }

    /// <summary>Parses the allowlist textarea: one "host" or "host:port" per line.</summary>
    private string BuildHostJson()
    {
        var raw = Form("cfgAllowlist") ?? string.Empty;
        var rows = new List<Dictionary<string, object>>();

        foreach (var line in raw.Split(new[] { '\n', '\r', ',' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var entry = line.Trim();
            if (entry.Length == 0) continue;

            int? port = null;
            var host = entry;

            var colon = entry.LastIndexOf(':');
            if (colon > 0 && int.TryParse(entry[(colon + 1)..], out var parsedPort))
            {
                host = entry[..colon];
                port = parsedPort;
            }

            rows.Add(new Dictionary<string, object> { ["host"] = host.ToLowerInvariant(), ["port"] = port });
        }

        return JsonSerializer.Serialize(rows);
    }

    private string BuildAuthJson()
    {
        var auth = new Dictionary<string, object>
        {
            ["AuthType"] = Form("cfgAuth") ?? "None",
            ["SecretStoreAlias"] = FormOrNull("cfgSecretProvider"),
            ["ClientIdSecretRef"] = FormOrNull("cfgClientIdRef"),
            ["ClientSecretSecretRef"] = FormOrNull("cfgClientSecretRef"),
            ["TokenEndpoint"] = FormOrNull("cfgTokenEndpoint"),
            ["Scope"] = FormOrNull("cfgScope"),
            ["Audience"] = FormOrNull("cfgAudience"),
            ["ApiKeyName"] = FormOrNull("cfgApiKeyName"),
            ["ApiKeyLocation"] = FormOrNull("cfgApiKeyLocation"),
            ["ApiKeySecretRef"] = FormOrNull("cfgApiKeyRef"),
            ["BearerTokenSecretRef"] = FormOrNull("cfgBearerRef"),
            ["UsernameSecretRef"] = FormOrNull("cfgUsernameRef"),
            ["PasswordSecretRef"] = FormOrNull("cfgPasswordRef"),
            ["CertificateSecretRef"] = FormOrNull("cfgCertificateRef"),
            ["CustomPluginKey"] = FormOrNull("cfgPluginKey"),
            ["AdditionalSettingsJson"] = null
        };

        return JsonSerializer.Serialize(auth);
    }

    private void Success(string message) => TempData["ApiConfigSuccess"] = message;
    private void Warn(string message) => TempData["ApiConfigWarning"] = message;
    private void Fail(string message) => TempData["ApiConfigError"] = message;
}

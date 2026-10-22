using System.Text.Json;
using AgenticPitstop.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using System.Data;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AgenticPitstop.Controllers;

/// <summary>
/// The Configuration workspace. Admin only, four steps:
///   1. Inflow Source   - how work enters the portal
///   2. Collections     - the data model, including generated tables
///   3. Inbox Setup     - which template columns the Inbox shows
///   4. Detail Setup    - how the review screen is composed
///
/// Lists arrive from the browser as JSON and are converted to table-valued
/// parameters here. No model classes are involved and no SQL text is built.
/// </summary>
[ApsAuthorize(ApsIdentity.RoleAdmin)]
public sealed class ConfigurationController : ApsController
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly InflowArchiveService _archive;
    private readonly ApsFileRoots _fileRoots;
    public ConfigurationController(ApsDb db, ApsIdentity identity, ApsMetadata meta,
                               IHttpClientFactory httpClientFactory,
                               InflowArchiveService archive,
                               ApsFileRoots fileRoots)
    : base(db, identity, meta)
    {
        _httpClientFactory = httpClientFactory;
        _archive = archive;
        _fileRoots = fileRoots;
    }

    protected override string ActiveScreenKey => "configuration";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    // =========================================================================
    // Screen
    // =========================================================================

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

    private static bool IsValidStatusProcedure(string procedureName)
    {
        return Regex.IsMatch(procedureName, "^[A-Za-z][A-Za-z0-9_]*$")
            && procedureName.StartsWith("APS_", StringComparison.Ordinal);
    }

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



    [HttpGet]
  
    public IActionResult Index(string tab = "inflow-source", int useCaseId = 0)
    {
        var useCases = LoadUseCases();
        var allowedUseCaseIds = useCases.AsEnumerable()
            .Select(r => ToInt(r, "UseCaseID"))
            .Where(id => id > 0)
            .ToHashSet();

        if (useCaseId > 0 && !allowedUseCaseIds.Contains(useCaseId))
            useCaseId = 0;
        if (useCaseId == 0)
            useCaseId = allowedUseCaseIds.FirstOrDefault();
        var config = Meta.InflowConfiguration(useCaseId);

        ViewBag.Tab = tab;
        ViewBag.Config = config;
        ViewBag.UseCases = useCases;
        ViewBag.UseCaseID = useCaseId;
        ViewBag.Headers = new DataTable();
        ViewBag.InflowColumns = Meta.InflowColumns(useCaseId);
       ViewBag.Versions = useCaseId > 0
    ? Db.Query("APS_InflowConfig_GetVersions", p => ApsDb.In(p, "@UseCaseID", useCaseId))
    : new DataTable();
        // The Collections list and the Detail Setup evidence picker both read
        // this, and both belong to the selected use case. With none selected
        // (useCaseId 0) nothing is filtered, so the screen still opens.
        ViewBag.Collections = Db.Query("APS_Collection_Get", p =>
            ApsDb.In(p, "@UseCaseID", useCaseId > 0 ? (int?)useCaseId : null));
        // Archive settings of the active template, so the Inflow Source tab can
        // show what was chosen last time.
        ViewBag.InflowArchive = Db.QuerySingle("APS_InflowConfig_GetArchive",
            p => ApsDb.In(p, "@UseCaseID", useCaseId));
        ViewBag.InflowArchiveProviders = _archive.CloudProviders.ToList();

        // The folders an administrator has registered, so the alias is chosen
        // rather than typed from memory.
        ViewBag.InflowFolderRoots = _fileRoots.ActiveAliases();
        ViewBag.ReferenceColumns = string.Equals(tab, "collections", StringComparison.OrdinalIgnoreCase)
         ? Db.Query("APS_Schema_GetReferenceColumns")
         : new DataTable();
        ViewBag.InboxColumns = Meta.InboxColumns(useCaseId);
        ViewBag.History = Meta.ConfigurationHistory(8);
        ViewBag.PreviewRecords = useCaseId > 0
            ? Db.Query("APS_InflowData_Get", p =>
            {
                ApsDb.In(p, "@UseCaseID", useCaseId);
                ApsDb.In(p, "@Top", 2);
            })
            : new DataTable();
        ViewBag.ReportCatalog = Db.Query("APS_ReportMaster_Get");
        ViewBag.SourceRegistry = Db.Query("APS_SourceRegistry_Get");
        ViewBag.ChecklistQuestions = Db.Query("APS_ChecklistQuestionMaster_Get");
        ViewBag.ManageCategories = Db.Query("APS_ManageCategory_Get");
        ViewBag.Clients = Db.Query("APS_ClientMaster_Get", p => ApsDb.In(p, "@activeonly", true));
        ViewBag.ChecklistSourceUsage = tab.Equals("source-registry", StringComparison.OrdinalIgnoreCase)
            ? Db.Query("APS_ChecklistQuestionMaster_Get")
            : new DataTable();

        if (string.Equals(tab, "detail-setup", StringComparison.OrdinalIgnoreCase))
        {
            ViewBag.DetailLayout = useCaseId > 0
                ? Db.QueryMultiple("APS_DetailLayout_Get", p => ApsDb.In(p, "@UseCaseID", useCaseId))
                : new DataSet();
            ViewBag.DetailTables = Db.Query("APS_Schema_GetDetailTables");
            ViewBag.DetailProcedures = Db.Query("APS_Schema_GetDetailProcedures");
            ViewBag.JoinableTables = Db.Query("APS_Schema_GetJoinableTables");
            ViewBag.JoinHints = Db.Query("APS_Schema_GetJoinHints");
            ViewBag.UseCaseQuestions = useCaseId > 0
            ? Db.Query("APS_ChecklistQuestion_GetWithMapping", p => ApsDb.In(p, "@UseCaseID", useCaseId))
            : new DataTable();
            ViewBag.RefreshPipelines = useCaseId > 0
                ? Db.Query("API_Config_Pipeline_GetForUseCase", p => ApsDb.In(p, "@UseCaseID", useCaseId))
                : new DataTable();
        }

        return View();
    }
    /// <summary>
    /// Read-only preview of the saved configuration, opened in its own tab.
    ///
    /// It reads through the same ApsMetadata calls the live screens use, so the
    /// preview cannot drift from what Inbox and Inbox Detail actually render.
    /// No record data is loaded: every grid shows its shape only, which keeps
    /// customer data out of a screen whose whole job is layout verification.
    /// </summary>
    [HttpGet]
    [HttpGet]
    public IActionResult Preview()
    {
        var useCases = LoadUseCases();
        var allowedUseCaseIds = useCases.AsEnumerable()
            .Select(r => ToInt(r, "UseCaseID"))
            .Where(id => id > 0)
            .ToHashSet();

        var useCaseId = allowedUseCaseIds.FirstOrDefault();
        var config = Meta.InflowConfiguration(useCaseId);

        ViewBag.Config = config;
        ViewBag.Headers = new DataTable();
        ViewBag.InflowColumns = Meta.InflowColumns(useCaseId);
        ViewBag.InboxColumns = Meta.InboxColumns(useCaseId);
        ViewBag.SourceRegistry = Db.Query("APS_SourceRegistry_Get");
        ViewBag.ChecklistQuestions = Db.Query("APS_ChecklistQuestionMaster_Get");
        ViewBag.Clients = Db.Query("APS_ClientMaster_Get", p => ApsDb.In(p, "@activeonly", true));

        // --- collections: the fields of each, keyed by collection id ----------
        var collections = Db.Query("APS_Collection_Get");
        var collectionFields = new Dictionary<short, DataTable>();
        foreach (DataRow collection in collections.Rows)
        {
            var cid = Convert.ToInt16(collection["CollectionID"]);
            collectionFields[cid] = Db.Query("APS_CollectionColumn_Get",
                p => ApsDb.In(p, "@CollectionID", (int)cid));
        }
        ViewBag.Collections = collections;
        ViewBag.CollectionFields = collectionFields;

        // --- reports: parameters per report -------------------------------------
        var reports = Db.Query("APS_ReportMaster_Get");
        var reportDetails = new Dictionary<short, DataSet>();
        foreach (DataRow report in reports.Rows)
        {
            var reportId = Convert.ToInt16(report["reportid"]);
            reportDetails[reportId] = Db.QueryMultiple("APS_ReportConfiguration_Get",
                p => ApsDb.In(p, "@reportid", reportId));
        }
        ViewBag.ReportCatalog = reports;
        ViewBag.ReportDetails = reportDetails;

        // --- detail setup --------------------------------------------------------
        var detailConfig = Meta.DetailConfiguration();
        ViewBag.DetailConfig = detailConfig;

        if (detailConfig == null)
        {
            ViewBag.PrimaryTabs = new List<DataRow>();
            ViewBag.SubTabs = new List<DataRow>();
            ViewBag.HeaderFields = new List<DataRow>();
            ViewBag.TabFields = new List<DataRow>();
        }
        else
        {
            var dcid = Convert.ToInt16(detailConfig["dcid"]);
            var mode = Convert.ToByte(detailConfig["mode"]);
            var allTabs = Meta.DetailTabs(dcid);
            var allFields = Meta.DetailFields(dcid);

            ViewBag.PrimaryTabs = allTabs.AsEnumerable()
                .Where(r => Convert.ToByte(r["mode"]) == mode && r["parenttabid"] == DBNull.Value)
                .OrderBy(r => Convert.ToInt16(r["sortorder"]))
                .ToList();
            ViewBag.SubTabs = allTabs.AsEnumerable()
                .Where(r => Convert.ToByte(r["mode"]) == mode && r["parenttabid"] != DBNull.Value)
                .OrderBy(r => Convert.ToInt16(r["sortorder"]))
                .ToList();
            ViewBag.HeaderFields = allFields.AsEnumerable()
                .Where(r => Convert.ToByte(r["mode"]) == mode && r["dtid"] == DBNull.Value)
                .OrderBy(r => Convert.ToInt16(r["sortorder"]))
                .ToList();
            ViewBag.TabFields = allFields.AsEnumerable()
                .Where(r => Convert.ToByte(r["mode"]) == mode && r["dtid"] != DBNull.Value)
                .ToList();
        }

        return View();
    }

    [HttpGet]
    public IActionResult SourceRegistry(short sourceid)
    {
        var source = Db.QuerySingle("APS_SourceRegistry_GetById",
            p => ApsDb.In(p, "@sourceid", sourceid));
        return source == null
            ? Fail("The selected source could not be found.")
            : Ok("Source loaded.", ApsMetadata.ToDictionary(source));
    }

    [HttpGet]
    public IActionResult ChecklistQuestion(short questionid)
    {
        var question = Db.QuerySingle("APS_ChecklistQuestionMaster_GetById",
            p => ApsDb.In(p, "@questionid", questionid));
        return question == null
            ? Fail("The selected checklist question could not be found.")
            : Ok("Checklist question loaded.", ApsMetadata.ToDictionary(question));
    }

    [HttpGet]
    public IActionResult OutputTypes()
    {
        var types = Db.Query("APS_OutputType_GetAll");
        return Ok(null, ApsMetadata.ToList(types));
    }

    [HttpGet]
    public IActionResult SourceTypes()
    {
        var types = Db.Query("APS_SourceType_GetAll");
        return Ok(null, ApsMetadata.ToList(types));
    }

    [HttpPost]
    public IActionResult SaveManageCategory(string categoryname, bool activestatus = true)
    {
        if (string.IsNullOrWhiteSpace(categoryname)) return Fail("Category name is required.");
        var output = Db.ExecuteWithOutput("APS_ManageCategory_Insert", p =>
        {
            ApsDb.In(p, "@categoryname", categoryname.Trim());
            ApsDb.In(p, "@active", activestatus);
            ApsDb.In(p, "@createdby", "360545");
        });
        return FromProcedure(output, "newcategoryid");
    }

    [HttpPost]
    public IActionResult DeleteManageCategory(short categoryid)
    {
        var output = Db.ExecuteWithOutput("APS_ManageCategory_Delete", p =>
        {
            ApsDb.In(p, "@categoryid", categoryid);
        });
        return FromProcedure(output);
    }

    [HttpPost]
    public IActionResult ImportSourceConfiguration(string configurationJson)
    {
        if (string.IsNullOrWhiteSpace(configurationJson)) return Fail("Choose a configuration file.");
        JsonDocument document;
        try { document = JsonDocument.Parse(configurationJson); }
        catch (JsonException) { return Fail("The configuration file is not valid JSON."); }

        using (document)
        {
            if (document.RootElement.TryGetProperty("sources", out var sources) && sources.ValueKind == JsonValueKind.Array)
            {
                foreach (var source in sources.EnumerateArray())
                {
                    var name = ReadString(source, "sourcename");
                    var code = ReadString(source, "sourcecode");
                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(code)) continue;
                    Db.Execute("APS_SourceRegistry_Save", p =>
                    {
                        ApsDb.In(p, "@sourceid", 0);
                        ApsDb.In(p, "@sourcename", name.Trim());
                        ApsDb.In(p, "@sourcecode", code.Trim());
                        ApsDb.In(p, "@sourcetype", ReadString(source, "sourcetype"));
                        ApsDb.In(p, "@accessinterface", ReadString(source, "accessinterface"));
                        ApsDb.In(p, "@ownerteam", ReadString(source, "owner"));
                        ApsDb.In(p, "@status", !string.Equals(ReadString(source, "status"), "false", StringComparison.OrdinalIgnoreCase));
                        ApsDb.In(p, "@description", ReadString(source, "description"));
                        ApsDb.In(p, "@actionby", ActionBy);
                    });
                }
            }
        }
        return Ok("Source configuration imported.");
    }

    [HttpPost]
    public IActionResult CloneChecklistQuestion(short questionid)
    {
        var question = Db.QuerySingle("APS_ChecklistQuestionMaster_GetById",
            p => ApsDb.In(p, "@questionid", questionid));
        if (question == null) return Fail("The selected checklist question could not be found.");

        string Cell(string name) => ApsMetadata.Cell(question, name);
        return SaveChecklistQuestion(0, Cell("category"), Cell("question") + " (Copy)",
            Cell("instructionprompt"), Cell("businessrulesprompt"),
            Cell("sourceapplicability"), Cell("outputtype"), Cell("displayorder"),
            Cell("developernotes"), true);
    }

    [HttpPost]
    public IActionResult SaveSourceRegistry(short sourceid, string sourcename, string sourcecode,
                                            string sourcetype, string accessinterface, string owner,
                                            string dataclassification,string authenticationconnection, 
                                            string description, string sourcedeveloperNotes,bool activestatus = true)
    {
        if (string.IsNullOrWhiteSpace(sourcename) || string.IsNullOrWhiteSpace(sourcecode))
            return Fail("Source name and source code are required.");

        var output = Db.ExecuteWithOutput("APS_SourceRegistry_Save", p =>
        {
            ApsDb.In(p, "@sourceid", sourceid);
            ApsDb.In(p, "@sourcename", sourcename.Trim());
            ApsDb.In(p, "@sourcecode", sourcecode.Trim());
            ApsDb.In(p, "@sourcetype", sourcetype?.Trim());
            ApsDb.In(p, "@accessinterface", accessinterface?.Trim());
            ApsDb.In(p, "@ownerteam", owner?.Trim());
            ApsDb.In(p, "@status", activestatus);           
            ApsDb.In(p, "@dataclassification", dataclassification?.Trim());
            ApsDb.In(p, "@authenticationConnection", authenticationconnection);
            ApsDb.In(p, "@description", description?.Trim());
            ApsDb.In(p, "@developerNotesConfiguration", sourcedeveloperNotes);
        });
        return FromProcedure(output);
    }

    [HttpPost]
    public IActionResult DeleteSourceRegistry(short sourceid)
    {
        var output = Db.ExecuteWithOutput("APS_SourceRegistry_Delete", p =>
        {
            ApsDb.In(p, "@sourceid", sourceid);
        });
        return FromProcedure(output);
    }

    [HttpPost]
    public IActionResult SaveChecklistQuestion(short questionid, string category, string question,
                                               string instructionprompt, string businessrulesprompt,
                                               string sourcesJson,string outputtype,string displayorder, string developernotes, bool activestatus = true)
    {
        if (string.IsNullOrWhiteSpace(question)) return Fail("Question is required.");
        var output = Db.ExecuteWithOutput("APS_ChecklistQuestionMaster_Save", p =>
        {
            ApsDb.In(p, "@questionid", questionid);
            ApsDb.In(p, "@question", question.Trim());
            ApsDb.In(p, "@category", category?.Trim());
            ApsDb.In(p, "@status", activestatus);
            ApsDb.In(p, "@instructionprompt", instructionprompt?.Trim());
            ApsDb.In(p, "@businessrulesprompt", businessrulesprompt?.Trim());
            ApsDb.In(p, "@sourceapplicability", sourcesJson);
            ApsDb.In(p, "@outputtype", outputtype);
            ApsDb.In(p, "@displayorder ", displayorder);
            ApsDb.In(p, "@developernotes", developernotes);
        });
        return FromProcedure(output);
    }

    [HttpPost]
    public IActionResult DeleteChecklistQuestion(short questionid)
    {
        var output = Db.ExecuteWithOutput("APS_ChecklistQuestionMaster_Delete", p =>
        {
            ApsDb.In(p, "@questionid", questionid);
        });
        return FromProcedure(output);
    }

    // =========================================================================
    // Step 5 - Report configuration
    // =========================================================================

    [HttpPost]
    public IActionResult SaveReportConfiguration(string reportname, string reportcode,
                                                  string description, string procedureschema,
                                                  string procedurename, string executionmode,
                                                  int? defaultpagesize, int? maxinteractiverows,
                                                  int? commandtimeout, string parametersJson)
    {
        if (string.IsNullOrWhiteSpace(reportname) || string.IsNullOrWhiteSpace(reportcode))
            return Fail("Report name and report code are required.");

        if (!IsValidIdentifier(procedureschema) || !IsValidIdentifier(procedurename))
            return Fail("Procedure schema and procedure name must be valid identifiers.");

        if (defaultpagesize is < 1 or > 100000 || maxinteractiverows is < 1 or > 1000000 ||
            commandtimeout is < 1 or > 3600)
            return Fail("Page size, maximum rows and timeout must contain valid positive values.");

        var report = Db.ExecuteWithOutput("APS_ReportMaster_Insert", p =>
        {
            ApsDb.In(p, "@reportname", reportname.Trim());
            ApsDb.In(p, "@reportcode", reportcode.Trim());
            ApsDb.In(p, "@description", description?.Trim());
            ApsDb.In(p, "@procedureschema", procedureschema.Trim());
            ApsDb.In(p, "@procedurename", procedurename.Trim());
            ApsDb.In(p, "@executionmode", executionmode?.Trim());
            ApsDb.In(p, "@defaultpagesize", defaultpagesize ?? 10);
            ApsDb.In(p, "@maxinteractiverows", maxinteractiverows ?? 5000);
            ApsDb.In(p, "@commandtimeout", commandtimeout ?? 120);
            ApsDb.In(p, "@isactive", 1);
            ApsDb.Out(p, "@reportid", SqlDbType.SmallInt);
            ApsDb.Out(p, "@message", SqlDbType.NVarChar, 400);
        });

        if (!report.TryGetValue("reportid", out var reportIdValue) || reportIdValue == null ||
            !short.TryParse(Convert.ToString(reportIdValue), out var reportId))
            return FromProcedure(report);

        short sortOrder = 0;
        foreach (var parameter in ParseArray(parametersJson))
        {
            var parameterName = ReadString(parameter, "parametername");
            if (string.IsNullOrWhiteSpace(parameterName) || !IsParameter(parameterName))
                return Fail("Every report parameter needs a valid name beginning with @.");

            Db.Execute("APS_ReportParameter_Insert", p =>
            {
                ApsDb.In(p, "@reportid", reportId);
                ApsDb.In(p, "@parametername", parameterName.Trim());
                ApsDb.In(p, "@displayname", ReadString(parameter, "displayname")?.Trim());
                ApsDb.In(p, "@datatype", ReadString(parameter, "datatype")?.Trim());
                ApsDb.In(p, "@controltype", ReadString(parameter, "controltype")?.Trim());
                ApsDb.In(p, "@defaultvalue", ReadString(parameter, "defaultvalue")?.Trim());
                ApsDb.In(p, "@isrequired", 1);
            });
        }

        return FromProcedure(report, "reportid");
    }

    [HttpGet]
    public IActionResult ReportConfiguration(short reportId)
    {
        var configuration = Db.QueryMultiple("APS_ReportConfiguration_Get",
            p => ApsDb.In(p, "@reportid", reportId));

        return Ok("Report configuration loaded.", new
        {
            definition = configuration.Tables.Count > 0
                ? ApsMetadata.ToList(configuration.Tables[0]).FirstOrDefault()
                : null,
            parameters = configuration.Tables.Count > 1
                ? ApsMetadata.ToList(configuration.Tables[1])
                : new List<Dictionary<string, object>>()
        });
    }

   
    [HttpGet]
    public IActionResult Collection(int id)
    {
        var master = Db.QuerySingle("APS_Collection_Get", p => ApsDb.In(p, "@CollectionID", id));
        if (master == null) return Fail("Collection not found.");

        var columns = Db.Query("APS_CollectionColumn_Get", p => ApsDb.In(p, "@CollectionID", id));

        return Ok(null, new
        {
            collection = ApsMetadata.ToDictionary(master),
            columns = ApsMetadata.ToList(columns)
        });
    }

    [HttpPost]
    public IActionResult InflowVersionColumns(int cid)
    {
        if (cid <= 0) return Fail("Version not found.");

        var table = Db.Query("APS_InflowColumnConfig_GetByConfig",
            p => ApsDb.In(p, "@InflowConfigID", cid));

        var data = table.AsEnumerable().Select(r => new
        {
            sortorder = Convert.ToInt32(r["SortOrder"]),
            columnname = Convert.ToString(r["ExcelColumnName"]),
            datatype = Convert.ToString(r["DataType"])
        }).ToList();

        return Ok("Columns loaded.", data);
    }

    // =========================================================================
    // Step 1 - Inflow source
    // =========================================================================

    [HttpPost]
    public IActionResult SaveInflow(int useCaseId, byte inflowtype, string filetype,
                                string templatename, bool acknowledged,
                                string columnsJson, byte replaceExisting)
    {
        if (useCaseId <= 0)
            return Fail("Select a client and process first.");

        if (!acknowledged)
            return Fail("Confirm you have reviewed the inflow process before saving.");

        // API inflow has columns in APS_InflowConfig but no procedure writes them yet.
        if (inflowtype == 1)
            return Fail("Automated (API) inflow cannot be saved yet. Use Excel upload for now.");

        if (string.IsNullOrWhiteSpace(templatename))
            return Fail("Upload the Excel template before saving.");

        if (!string.Equals(filetype, "Excel", StringComparison.OrdinalIgnoreCase))
            return Fail("Only Excel is supported in this version.");

        var problem = BuildTemplateColumns(columnsJson, out var columns, out var templateHash);
        if (problem != null) return Fail(problem);

        // After "Apply columns" the page reloads and the discovered list is empty.
        // The active version is already saved, so there is nothing more to write.
        if (columns.Rows.Count == 0)
        {
            return Meta.InflowConfiguration(useCaseId) != null
                ? Ok("The active template is already saved.")
                : Fail("Upload the Excel template and apply its columns first.");
        }

        var fileName = Path.GetFileName(templatename.Trim());

        var outputs = Db.ExecuteWithOutput("APS_InflowConfig_Save", p =>
        {
            ApsDb.In(p, "@UseCaseID", useCaseId);
            ApsDb.In(p, "@InflowSourceType", (byte)2);          // 2 = Excel upload
            ApsDb.In(p, "@FileType", "Excel");
            ApsDb.In(p, "@TemplateName", fileName);
            ApsDb.In(p, "@TemplateFileName", fileName);
            ApsDb.In(p, "@TemplateHash", templateHash);
            ApsDb.In(p, "@IsAcknowledged", (byte)1);
            ApsDb.In(p, "@ReplaceExisting", replaceExisting == 1 ? (byte)1 : (byte)0);
            ApsDb.Table(p, "@Columns", "dbo.APS_InflowColumnType", columns);
            ApsDb.In(p, "@ActionBy", ActionBy);
            ApsDb.Out(p, "@NewInflowConfigID", SqlDbType.Int);
            ApsDb.Out(p, "@AlreadyExists", SqlDbType.TinyInt);
            ApsDb.Out(p, "@Message", SqlDbType.VarChar, 200);
        });

        // The view shows its "Replace active template" dialog when it sees this flag,
        // then posts again with replaceExisting = 1.
        if (outputs.TryGetValue("AlreadyExists", out var exists) && Convert.ToInt32(exists ?? 0) == 1)
        {
            return Fail(Convert.ToString(outputs["Message"]), new { confirmReplace = true });
        }

        return FromProcedure(outputs, "NewInflowConfigID");
    }

    /// <summary>
    /// Where uploaded workbooks are kept for this use case.
    ///
    /// Deliberately separate from SaveInflow: the existing save path and its
    /// procedure are left exactly as they are, and the screen calls this straight
    /// afterwards. Credentials are never accepted here - only a bucket, a region,
    /// a prefix, and optionally a reference to a secret held elsewhere.
    /// </summary>
    [HttpPost]
    public IActionResult SaveInflowArchive(int useCaseId, string target, string provider,
                                           string root, string bucket, string region,
                                           string prefix, string secretRef)
    {
        if (useCaseId <= 0) return Fail("Select a client and process first.");

        var settings = new InflowArchiveSettings
        {
            Target = (target ?? InflowArchiveSettings.TargetNone).Trim().ToUpperInvariant(),
            Provider = provider?.Trim().ToUpperInvariant(),
            Root = root?.Trim(),
            Bucket = bucket?.Trim(),
            Region = region?.Trim(),
            Prefix = prefix?.Trim(),
            SecretRef = secretRef?.Trim()
        };

        // A value that looks like a credential is refused outright, whichever box
        // it was typed into. The same rule the API Configurator applies.
        foreach (var value in new[] { settings.Bucket, settings.Region, settings.Prefix })
            if (!string.IsNullOrEmpty(value) && value.StartsWith("AKIA", StringComparison.OrdinalIgnoreCase))
                return Fail("Credential values are not accepted here. Use a secret reference.");

        var problem = _archive.Validate(settings);
        if (problem != null) return Fail(problem);

        var json = settings.IsEnabled
            ? System.Text.Json.JsonSerializer.Serialize(settings)
            : null;

        var outputs = Db.ExecuteWithOutput("APS_InflowConfig_SaveArchive", p =>
        {
            ApsDb.In(p, "@UseCaseID", useCaseId);
            ApsDb.In(p, "@ArchiveJson", json);
            ApsDb.In(p, "@ActionBy", ActionBy);
            ApsDb.Out(p, "@Success", SqlDbType.TinyInt);
            ApsDb.Out(p, "@Message", SqlDbType.VarChar, 200);
        });

        return FromProcedure(outputs);
    }

    /// <summary>
    /// Reads the uploaded workbook's header row and returns the discovered
    /// columns. Nothing is persisted here — the Admin reviews the columns and
    /// then applies them.
    /// </summary>
    [HttpPost]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> DiscoverTemplate(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return Fail("Choose an Excel template to upload.");

        if (!Path.GetExtension(file.FileName).Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
            return Fail("Only .xlsx templates are supported in this version.");

        try
        {
            using var stream = new MemoryStream();
            await file.CopyToAsync(stream);
            stream.Position = 0;

            var rawHeaders = ApsExcel.ReadRawHeaders(stream);

            // Row 1 is the header row. An empty first row means the data starts
            // higher up than the template assumes, and every column would be
            // named after a value instead of a header.
            if (rawHeaders.Count == 0 || rawHeaders.All(string.IsNullOrWhiteSpace))
                return Fail("Row 1 is empty. The first row must contain the column headers.");

            // A blank header between two filled ones is a gap, not an unnamed
            // column. The Excel letters are named so the Admin can find it.
            var blanks = rawHeaders
                .Select((header, index) => new { header, index })
                .Where(x => string.IsNullOrWhiteSpace(x.header))
                .Select(x => ExcelColumnName(x.index))
                .ToList();

            if (blanks.Count > 0)
                return Fail($"Row 1 has no header in column {string.Join(", ", blanks)}. " +
                            "Every column up to the last one must be named.");

            // Spaces inside a header are removed rather than rejected, so
            // "Policy Number" becomes PolicyNumber.
            var columns = rawHeaders
                .Select(header => new string(header.Where(c => !char.IsWhiteSpace(c)).ToArray()))
                .ToList();

            // Whatever is left must stand as a database column name.
            var invalid = columns.Where(c => !IsValidIdentifier(c)).ToList();
            if (invalid.Count > 0)
                return Fail($"These column headers are not usable as database columns: {string.Join(", ", invalid)}. " +
                            "Use letters, numbers and underscore, starting with a letter.");

            // Removing spaces can collide two headers - "Policy No" and
            // "PolicyNo" - and a duplicate column would silently overwrite.
            var duplicate = columns
                .GroupBy(c => c, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(g => g.Count() > 1);

            if (duplicate != null)
                return Fail($"Column '{duplicate.Key}' appears more than once. Each header must be unique.");

            return Ok($"{columns.Count} column(s) discovered.", new
            {
                templatename = Path.GetFileName(file.FileName),
                columns
            });
        }
        catch (InvalidDataException)
        {
            return Fail("That file could not be read as an .xlsx workbook.");
        }
    }

    /// <summary>
    /// Saves the discovered columns as a new template version for the use case.
    /// APS_InflowConfig_Save deactivates the current version and inserts the next
    /// one in a single transaction. An identical column set is not a new version.
    /// </summary>

    /// <summary>
    /// Turns the discovered column list into an APS_InflowColumnType table and a
    /// server-side hash of the ordered names. Returns an error message, or null.
    /// </summary>
    private string BuildTemplateColumns(string columnsJson, out DataTable columns, out string templateHash)
    {
        columns = ApsDb.NewInflowColumnList();
        templateHash = null;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var order = 0;

        foreach (var element in ParseArray(columnsJson))
        {
            var name = element.ValueKind == JsonValueKind.String
                ? element.GetString()
                : ReadString(element, "name");

            if (string.IsNullOrWhiteSpace(name)) continue;
            name = name.Trim();

            if (!IsValidIdentifier(name))
                return $"Column '{name}' must start with a letter and contain only letters, numbers or underscore.";

            if (!seen.Add(name))
                return $"Column '{name}' appears more than once in the template.";

            // ExcelColumnName, FieldCode, DataType, ColumnLength, IsRequired, ShowInInbox, ShowInDetail, SortOrder
            columns.Rows.Add(name, name, "varchar", DBNull.Value, (byte)0, (byte)1, (byte)1, ++order);
        }

        if (columns.Rows.Count == 0) return null;

        var canonical = string.Join("\n", columns.AsEnumerable().Select(r => Convert.ToString(r["ExcelColumnName"])));
        templateHash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(canonical)));

        return null;
    }
    [HttpPost]
    public IActionResult ApplyTemplate(string columnsJson, int useCaseId, string templatename)
    {
        if (useCaseId <= 0)
            return Fail("Select a use case before applying template columns.");

        if (string.IsNullOrWhiteSpace(templatename))
            return Fail("Upload the Excel template first.");

        var columns = ApsDb.NewInflowColumnList();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var order = 0;

        foreach (var element in ParseArray(columnsJson))
        {
            var name = element.ValueKind == JsonValueKind.String
                ? element.GetString()
                : ReadString(element, "name");

            if (string.IsNullOrWhiteSpace(name)) continue;
            name = new string(name.Where(c => !char.IsWhiteSpace(c)).ToArray());

            if (!IsValidIdentifier(name))
                return Fail($"Column '{name}' must start with a letter and contain only letters, numbers or underscore.");

            if (!seen.Add(name))
                return Fail($"Column '{name}' appears more than once in the template.");

            // ExcelColumnName, FieldCode, DataType, ColumnLength, IsRequired, ShowInInbox, ShowInDetail, SortOrder
            columns.Rows.Add(name, name, "varchar", DBNull.Value, (byte)0, (byte)1, (byte)1, ++order);
        }

        if (columns.Rows.Count == 0)
            return Fail("No columns were supplied.");

        // Hash of the ordered column list, computed on the server so the browser
        // cannot claim two different templates are the same one.
        var canonical = string.Join("\n", columns.AsEnumerable().Select(r => Convert.ToString(r["ExcelColumnName"])));
        var templateHash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));

        var fileName = Path.GetFileName(templatename.Trim());

        var outputs = Db.ExecuteWithOutput("APS_InflowConfig_Save", p =>
        {
            ApsDb.In(p, "@UseCaseID", useCaseId);
            ApsDb.In(p, "@InflowSourceType", (byte)2);          // 2 = Excel upload
            ApsDb.In(p, "@FileType", "Excel");
            ApsDb.In(p, "@TemplateName", fileName);
            ApsDb.In(p, "@TemplateFileName", fileName);
            ApsDb.In(p, "@TemplateHash", templateHash);
            ApsDb.In(p, "@IsAcknowledged", (byte)1);            // confirmed in the dialog
            ApsDb.In(p, "@ReplaceExisting", (byte)1);           // the dialog warned about replacing
            ApsDb.Table(p, "@Columns", "dbo.APS_InflowColumnType", columns);
            ApsDb.In(p, "@ActionBy", ActionBy);
            ApsDb.Out(p, "@NewInflowConfigID", SqlDbType.Int);
            ApsDb.Out(p, "@AlreadyExists", SqlDbType.TinyInt);
            ApsDb.Out(p, "@Message", SqlDbType.VarChar, 200);
        });

        return FromProcedure(outputs, "NewInflowConfigID");
    }

    /// <summary>
    /// Reachability probe. Runs server side so configured secrets never reach
    /// the browser, and so the check is not blocked by CORS.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> CheckConnectivity(string apiendpoint)
    {
        if (!AgentsController.IsHttpUrl(apiendpoint))
            return Fail("Enter an API endpoint starting with http:// or https://.");

        var client = _httpClientFactory.CreateClient("ApsInflowProbe");

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, apiendpoint);
            using var response = await client.SendAsync(request);

            return Ok($"Endpoint reachable (HTTP {(int)response.StatusCode}).",
                      new { reachable = true, status = (int)response.StatusCode });
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return Ok("Endpoint not reachable.", new { reachable = false, status = 0 });
        }
    }

    /// <summary>
    /// Sends the configured request and returns the raw response for review.
    /// Header values come from the database, not from the form, so a secret is
    /// never round-tripped through the browser to be tested.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> TestApi()
    {
        var useCases = LoadUseCases();
        var allowedUseCaseIds = useCases.AsEnumerable()
            .Select(r => ToInt(r, "UseCaseID"))
            .Where(id => id > 0)
            .ToHashSet();



        var useCaseId = allowedUseCaseIds.First();

        var config = Meta.InflowConfiguration(useCaseId);
        if (config == null) return Fail("Save the inflow configuration before testing it.");
        if (Convert.ToByte(config["inflowtype"]) != 1) return Fail("The configured inflow source is not an API.");

        var endpoint = Convert.ToString(config["apiendpoint"]);
        if (!AgentsController.IsHttpUrl(endpoint)) return Fail("The configured endpoint is not a valid URL.");

        var cid = Convert.ToInt16(config["cid"]);
        var body = Convert.ToString(config["requestbody"]);
        var client = _httpClientFactory.CreateClient("ApsInflowProbe");

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(body ?? "{}", Encoding.UTF8, "application/json")
        };

        foreach (DataRow header in Meta.ConfigHeaders(cid).Rows)
        {
            var chid = Convert.ToInt16(header["chid"]);
            var secret = Db.QuerySingle("APS_ConfigHeader_GetSecret", p => ApsDb.In(p, "@chid", chid));
            if (secret == null) continue;

            var key = Convert.ToString(secret["headerkey"]);
            var value = Convert.ToString(secret["headervalue"]);
            if (string.IsNullOrWhiteSpace(key)) continue;

            request.Headers.TryAddWithoutValidation(key, value);
        }

        try
        {
            using var response = await client.SendAsync(request);
            var payload = await response.Content.ReadAsStringAsync();

            // Cap the preview so a large payload cannot flood the viewer.
            if (payload.Length > 8000)
                payload = payload[..8000] + "\n... response truncated for display ...";

            Db.Execute("APS_Configuration_SetApiStatus", p =>
            {
                ApsDb.In(p, "@cid", cid);
                ApsDb.In(p, "@apistatus", response.IsSuccessStatusCode ? "healthy" : "unhealthy");
            });

            return Ok($"HTTP {(int)response.StatusCode} {response.ReasonPhrase}",
                      new { status = (int)response.StatusCode, body = payload });
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            Db.Execute("APS_Configuration_SetApiStatus", p =>
            {
                ApsDb.In(p, "@cid", cid);
                ApsDb.In(p, "@apistatus", "unhealthy");
            });

            return Fail("The endpoint could not be reached: " + exception.Message);
        }
    }

    /// <summary>Returns one header value for the show/hide toggle.</summary>
    [HttpPost]
    public IActionResult RevealHeader(short chid)
    {
        var row = Db.QuerySingle("APS_ConfigHeader_GetSecret", p => ApsDb.In(p, "@chid", chid));
        return row == null
            ? Fail("Header not found.")
            : Ok(null, new { value = Convert.ToString(row["headervalue"]) });
    }

    // =========================================================================
    // Step 2 - Collections and fields
    // =========================================================================

    private static readonly HashSet<string> CollectionSqlTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "bigint", "int", "smallint", "tinyint", "bit", "decimal", "numeric", "money", "smallmoney", "float", "real",
        "date", "time", "datetime", "datetime2", "smalldatetime", "datetimeoffset",
        "char", "varchar", "nchar", "nvarchar", "binary", "varbinary", "uniqueidentifier", "xml"
    };

    private static readonly HashSet<string> CollectionLengthTypes = new(StringComparer.OrdinalIgnoreCase)
        { "char", "varchar", "nchar", "nvarchar", "binary", "varbinary" };

    private static readonly HashSet<string> CollectionScaleTypes = new(StringComparer.OrdinalIgnoreCase)
        { "time", "datetime2", "datetimeoffset" };

    private static readonly HashSet<string> CollectionSystemColumns = new(StringComparer.OrdinalIgnoreCase)
        { "InflowDataID", "UseCaseID", "IsActive", "CreatedDate", "CreatedBy", "ModifiedDate", "ModifiedBy" };
    /// <summary>Zero-based index to Excel's column letters: 0 becomes A, 26 becomes AA.</summary>
    private static string ExcelColumnName(int index)
    {
        var name = string.Empty;
        for (var value = index + 1; value > 0; value = (value - 1) / 26)
            name = (char)('A' + (value - 1) % 26) + name;
        return name;
    }
    /// <summary>
    /// Saves the definition only. System columns are added here on the server,
    /// never taken from the browser, so every collection table has the same spine.
    /// </summary>
    [HttpPost]
    public IActionResult SaveCollection(int collectionId, string collectionname, string description,
                                        int useCaseId, string fieldsJson)
    {
        collectionname = collectionname?.Trim();

        if (string.IsNullOrWhiteSpace(collectionname))
            return Fail("Collection name is required.");

        if (!IsValidCollectionName(collectionname) || collectionname.Length > 60)
            return Fail("Collection name must start with a letter, use letters and numbers only, and be at most 60 characters.");

        if (useCaseId <= 0)
            return Fail("Select the use case this collection belongs to.");

        var tableName = "APS_Collection_" + collectionname;
        var keyColumn = collectionname + "ID";

        var references = Db.Query("APS_Schema_GetReferenceColumns").AsEnumerable()
            .GroupBy(r => Convert.ToString(r["TableName"]) + "." + Convert.ToString(r["ColumnName"]),
                     StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var columns = ApsDb.NewCollectionColumnList();
        var order = 0;

        void AddSystem(string name, string type, object length, bool nullable, string constraint,
                       string defaultValue = null, string refTable = null, string refColumn = null,
                       bool identity = false)
        {
            columns.Rows.Add(name, type, length ?? DBNull.Value, DBNull.Value, DBNull.Value,
                             nullable ? (byte)1 : (byte)0, constraint,
                             (object)defaultValue ?? DBNull.Value, DBNull.Value,
                             (object)refTable ?? DBNull.Value, (object)refColumn ?? DBNull.Value,
                             identity ? (byte)1 : (byte)0, (byte)1, ++order);
        }

        // --- leading system columns ---------------------------------------------
        AddSystem(keyColumn, "int", null, false, "PK", identity: true);
        AddSystem("InflowDataID", "int", null, false, "FK", refTable: "APS_InflowData", refColumn: "InflowDataID");
        AddSystem("UseCaseID", "int", null, false, "FK", useCaseId.ToString(), "APS_UseCaseMaster", "UseCaseID");

        // --- user fields -----------------------------------------------------------
        var reserved = new HashSet<string>(CollectionSystemColumns, StringComparer.OrdinalIgnoreCase) { keyColumn };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var element in ParseArray(fieldsJson))
        {
            var name = ReadString(element, "name")?.Trim();
            if (string.IsNullOrEmpty(name)) continue;

            if (!IsValidIdentifier(name) || name.Length > 60)
                return Fail($"Field '{name}' must start with a letter, use letters, numbers or underscore, and be at most 60 characters.");

            if (reserved.Contains(name))
                return Fail($"'{name}' is a system column and is added automatically.");

            if (!seen.Add(name))
                return Fail($"Field '{name}' is listed more than once.");

            if ((tableName + "_" + name + "_DF").Length > 128)
                return Fail($"Field '{name}' is too long to build its constraint name.");

            var constraint = (ReadString(element, "constraint") ?? "NONE").Trim().ToUpperInvariant();
            if (constraint is not ("NONE" or "FK" or "UQ" or "C"))
                return Fail($"Field '{name}': choose a valid constraint.");

            string type;
            object length = DBNull.Value, precision = DBNull.Value, scale = DBNull.Value;
            object refTableValue = DBNull.Value, refColumnValue = DBNull.Value, checkValue = DBNull.Value;

            if (constraint == "FK")
            {
                var refTable = ReadString(element, "refTable")?.Trim();
                var refColumn = ReadString(element, "refColumn")?.Trim();

                if (string.IsNullOrEmpty(refTable) || string.IsNullOrEmpty(refColumn) ||
                    !references.TryGetValue(refTable + "." + refColumn, out var target))
                    return Fail($"Field '{name}': choose a reference table and a key column.");

                // A foreign key must match the referenced key's type exactly.
                type = Convert.ToString(target["DataTypeName"]).ToLowerInvariant();
                if (!CollectionSqlTypes.Contains(type))
                    return Fail($"Field '{name}': the referenced column uses an unsupported data type.");

                if (CollectionLengthTypes.Contains(type)) length = Convert.ToInt32(target["ColumnLength"]);
                if (type is "decimal" or "numeric")
                {
                    precision = Convert.ToByte(target["NumericPrecision"]);
                    scale = Convert.ToByte(target["NumericScale"]);
                }
                if (CollectionScaleTypes.Contains(type)) scale = Convert.ToByte(target["NumericScale"]);

                refTableValue = refTable;
                refColumnValue = refColumn;
            }
            else
            {
                type = (ReadString(element, "dataType") ?? string.Empty).Trim().ToLowerInvariant();
                if (!CollectionSqlTypes.Contains(type))
                    return Fail($"Field '{name}': choose a data type.");

                if (CollectionLengthTypes.Contains(type))
                {
                    var raw = (ReadString(element, "length") ?? string.Empty).Trim();
                    var max = type is "nchar" or "nvarchar" ? 4000 : 8000;
                    var allowsMax = type is "varchar" or "nvarchar" or "varbinary";

                    if (allowsMax && raw.Equals("max", StringComparison.OrdinalIgnoreCase)) length = -1;
                    else if (int.TryParse(raw, out var n) && n >= 1 && n <= max) length = n;
                    else return Fail($"Field '{name}': enter a length between 1 and {max}{(allowsMax ? ", or MAX" : "")}.");
                }
                else if (type is "decimal" or "numeric")
                {
                    var p = ReadInt(element, "precision") ?? 18;
                    var s = ReadInt(element, "scale") ?? 0;
                    if (p < 1 || p > 38 || s < 0 || s > p)
                        return Fail($"Field '{name}': precision must be 1-38 and scale between 0 and the precision.");
                    precision = (byte)p;
                    scale = (byte)s;
                }
                else if (CollectionScaleTypes.Contains(type))
                {
                    var s = ReadInt(element, "scale");
                    if (s.HasValue)
                    {
                        if (s < 0 || s > 7) return Fail($"Field '{name}': fractional-second scale must be 0-7.");
                        scale = (byte)s.Value;
                    }
                }

                if (constraint == "C")
                {
                    var expression = ReadString(element, "check")?.Trim();
                    var problem = ValidateCheckExpression(expression);
                    if (problem != null) return Fail($"Field '{name}': {problem}");
                    checkValue = expression;
                }
            }

            var defaultValue = ReadString(element, "defaultValue")?.Trim();
            if (defaultValue?.Length > 200)
                return Fail($"Field '{name}': the default value is longer than 200 characters.");

            columns.Rows.Add(name, type, length, precision, scale,
                             ReadBool(element, "nullable") ? (byte)1 : (byte)0, constraint,
                             string.IsNullOrEmpty(defaultValue) ? DBNull.Value : defaultValue,
                             checkValue, refTableValue, refColumnValue,
                             (byte)0, (byte)0, ++order);
        }

        if (seen.Count == 0)
            return Fail("Add at least one field to the collection.");

        // --- trailing audit columns --------------------------------------------------
        AddSystem("IsActive", "tinyint", null, false, "NONE", "1");
        AddSystem("CreatedDate", "datetime", null, false, "NONE", "GETDATE()");
        AddSystem("CreatedBy", "varchar", 30, false, "NONE");
        AddSystem("ModifiedDate", "datetime", null, true, "NONE");
        AddSystem("ModifiedBy", "varchar", 30, true, "NONE");

        var outputs = Db.ExecuteWithOutput("APS_Collection_Save", p =>
        {
            ApsDb.In(p, "@CollectionID", collectionId);
            ApsDb.In(p, "@CollectionName", collectionname);
            ApsDb.In(p, "@TableName", tableName);
            ApsDb.In(p, "@Description", string.IsNullOrWhiteSpace(description) ? null : description.Trim());
            ApsDb.In(p, "@UseCaseID", useCaseId);
            ApsDb.Table(p, "@Columns", "dbo.APS_CollectionColumnType", columns);
            ApsDb.In(p, "@ActionBy", ActionBy);
            ApsDb.Out(p, "@NewCollectionID", SqlDbType.Int);
            ApsDb.Out(p, "@Message", SqlDbType.VarChar, 200);
        });

        return FromProcedure(outputs, "NewCollectionID");
    }

    /// <summary>Creates the table, or adds new columns and constraints to it.</summary>
    [HttpPost]
    public IActionResult GenerateCollection(int collectionId)
    {
        if (collectionId <= 0)
            return Fail("Save the collection before creating its table.");

        var outputs = Db.ExecuteWithOutput("APS_Collection_Generate", p =>
        {
            ApsDb.In(p, "@CollectionID", collectionId);
            ApsDb.In(p, "@ActionBy", ActionBy);
            ApsDb.Out(p, "@Success", SqlDbType.TinyInt);
            ApsDb.Out(p, "@Message", SqlDbType.VarChar, 400);
        });

        return FromProcedure(outputs);
    }

    private static int? ReadInt(JsonElement element, string property) =>
        int.TryParse(ReadString(element, property), out var value) ? value : (int?)null;

    private static readonly System.Text.RegularExpressions.Regex CheckBlockedWords = new(
        @"\b(select|insert|update|delete|merge|drop|alter|create|exec|execute|declare|grant|revoke|deny|" +
        @"truncate|shutdown|waitfor|openrowset|openquery|opendatasource|backup|restore|use|go|kill|dbcc|xp_\w*|sp_\w*)\b",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>
    /// A check expression is placed inside CHECK ( ... ), so it must never be able
    /// to close that bracket early or run a statement.
    /// </summary>
    private static string ValidateCheckExpression(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
            return "enter the check expression, for example Amount >= 0.";
        if (expression.Length > 500)
            return "the check expression is longer than 500 characters.";
        if (expression.IndexOfAny(new[] { ';', '[', ']', '"' }) >= 0 ||
            expression.Contains("--") || expression.Contains("/*") || expression.Contains("*/"))
            return "the check expression cannot contain ; [ ] \" or comments.";

        var outside = new System.Text.StringBuilder();
        var depth = 0;
        var inString = false;

        for (var i = 0; i < expression.Length; i++)
        {
            var ch = expression[i];
            if (inString)
            {
                if (ch == '\'')
                {
                    if (i + 1 < expression.Length && expression[i + 1] == '\'') { i++; continue; }
                    inString = false;
                }
                continue;
            }

            if (ch == '\'') { inString = true; outside.Append(' '); continue; }
            if (ch == '(') depth++;
            else if (ch == ')' && --depth < 0) return "the check expression has an unmatched ')'.";
            outside.Append(ch);
        }

        if (inString) return "the check expression has an unclosed quote.";
        if (depth != 0) return "the check expression has an unmatched '('.";
        if (CheckBlockedWords.IsMatch(outside.ToString()))
            return "the check expression can only compare values; statements are not allowed.";

        return null;
    }

    // =========================================================================
    // Step 3 - Inbox layout
    // =========================================================================

    [HttpPost]
    public IActionResult SaveInboxConfig(string columnsJson, int useCaseId)
    {
        if (useCaseId <= 0)
            return Fail("Select a client and process first.");

        // FieldCode -> Excel header, from the active template of this use case.
        var displayNames = Meta.InflowColumns(useCaseId).AsEnumerable()
            .GroupBy(r => Convert.ToString(r["FieldCode"]), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key,
                          g => Convert.ToString(g.First()["ExcelColumnName"]),
                          StringComparer.OrdinalIgnoreCase);

        if (displayNames.Count == 0)
            return Fail("This use case has no active template. Apply the template columns first.");

        // Shaped like dbo.APS_InboxColumnType: FieldCode, DisplayName, SortOrder
        var columns = new DataTable();
        columns.Columns.Add("FieldCode", typeof(string));
        columns.Columns.Add("DisplayName", typeof(string));
        columns.Columns.Add("SortOrder", typeof(int));

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var order = 0;

        foreach (var element in ParseArray(columnsJson))
        {
            var name = element.ValueKind == JsonValueKind.String
                ? element.GetString()
                : ReadString(element, "name");

            if (string.IsNullOrWhiteSpace(name)) continue;
            name = new string(name.Where(c => !char.IsWhiteSpace(c)).ToArray());

            if (!displayNames.TryGetValue(name, out var displayName))
                return Fail($"'{name}' is not a column of the active template.");

            if (!seen.Add(name)) continue;

            columns.Rows.Add(name, displayName, ++order);
        }

        if (columns.Rows.Count == 0)
            return Fail("Select at least one column before saving the Inbox layout.");

        var outputs = Db.ExecuteWithOutput("APS_InboxConfig_Save", p =>
        {
            ApsDb.In(p, "@UseCaseID", useCaseId);
            ApsDb.In(p, "@GridType", (byte)1);                 // 1 = uploaded rows grid
            ApsDb.In(p, "@StatusProcedureName", null);
            ApsDb.Table(p, "@Columns", "dbo.APS_InboxColumnType", columns);
            ApsDb.In(p, "@ActionBy", ActionBy);
            ApsDb.Out(p, "@Success", SqlDbType.TinyInt);
            ApsDb.Out(p, "@Message", SqlDbType.VarChar, 200);
        });

        return FromProcedure(outputs);
    }

    // =========================================================================
    // Step 4 - Detail layout (tiles + tabs, per use case and workspace)
    // =========================================================================

    private static readonly (string Key, string Label)[] DocViewerMap =
    {
        ("docPath", "DocPath"), ("docName", "DocName"), ("inflowDataId", "InflowDataID"),
        ("field", "Field"), ("value", "Value")
    };

    private static readonly string[] DocViewerSections = { "documents", "preview", "fields" };

    /// <summary>What a layout may point at, read from the database at save time.</summary>
    private sealed class DetailCatalog
    {
        public Dictionary<string, HashSet<string>> Tables { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, HashSet<string>> Procedures { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> InflowFields { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<int> QuestionIds { get; } = new();

        public HashSet<string> ColumnsOf(string sourceType, string sourceName) => sourceType switch
        {
            "INFLOW" => InflowFields,
            "TABLE" => sourceName != null && Tables.TryGetValue(sourceName, out var t) ? t : null,
            "PROC" => sourceName != null && Procedures.TryGetValue(sourceName, out var p) ? p : null,
            _ => null
        };
    }

    private DetailCatalog LoadDetailCatalog(int useCaseId)
    {
        var catalog = new DetailCatalog();

        foreach (DataRow r in Db.Query("APS_Schema_GetDetailTables").Rows)
        {
            var table = Convert.ToString(r["TableName"]);
            if (!catalog.Tables.TryGetValue(table, out var columns))
                catalog.Tables[table] = columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            columns.Add(Convert.ToString(r["ColumnName"]));
        }

        foreach (DataRow r in Db.Query("APS_Schema_GetDetailProcedures").Rows)
        {
            if (r["ColumnName"] == DBNull.Value) continue;   // SQL Server could not describe it
            var procedure = Convert.ToString(r["ProcedureName"]);
            if (!catalog.Procedures.TryGetValue(procedure, out var columns))
                catalog.Procedures[procedure] = columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            columns.Add(Convert.ToString(r["ColumnName"]));
        }

        foreach (DataRow r in Meta.InflowColumns(useCaseId).Rows)
            catalog.InflowFields.Add(Convert.ToString(r["FieldCode"]));

        foreach (DataRow r in Db.Query("APS_ChecklistQuestionMaster_Get").Rows)
            if (int.TryParse(ApsMetadata.Cell(r, "QuestionId"), out var id))
                catalog.QuestionIds.Add(id);

        return catalog;
    }

    [HttpPost]
    public IActionResult SaveDetailLayout(int useCaseId, byte workspaceMode, string tilesJson, string tabsJson,string tileSourceSpecJson = null)
    {
        if (useCaseId <= 0)
            return Fail("Select a client and process first.");

        if (workspaceMode is not (1 or 2))
            return Fail("Select a workspace type.");

        var catalog = LoadDetailCatalog(useCaseId);

        // --- tiles ------------------------------------------------------------------
        var tiles = ApsDb.NewDetailLayoutTileList();

        var sharedProblem = ValidateSpec(tileSourceSpecJson, out var sharedSpec);
        if (sharedProblem != null) return Fail($"Shared tile source: {sharedProblem}");

        foreach (var element in ParseArray(tilesJson))
        {
            var number = tiles.Rows.Count + 1;
            var title = ReadString(element, "title")?.Trim();
            if (string.IsNullOrEmpty(title) || title.Length > 100)
                return Fail($"Tile {number}: enter a title of up to 100 characters.");

            var icon = ReadString(element, "icon");
            if (!ApsMetadata.DetailIcons.Contains(icon))
                return Fail($"Tile '{title}': choose an icon from the list.");

            var accent = ReadString(element, "accent");
            if (!ApsMetadata.DetailAccents.Contains(accent))
                return Fail($"Tile '{title}': choose an accent colour.");

            var format = ReadString(element, "valueFormat") ?? "text";
            if (!ApsMetadata.DetailValueFormats.Contains(format))
                return Fail($"Tile '{title}': choose a value format.");

            var tileSpec = ReadString(element, "specJson");
            var subtitleSpec = ReadString(element, "subtitleSpecJson");
            var valueColumn = ReadString(element, "valueColumn");
            var subtitleColumn = ReadString(element, "subtitleColumn");

            string sourceType, sourceName;

            if (!string.IsNullOrWhiteSpace(tileSpec) || !string.IsNullOrWhiteSpace(sharedSpec))
            {
                // A spec, or the layout's shared source: the columns come from it,
                // and SQL verifies them against the catalogue when the tile is read.
                var specProblem = ValidateSpec(tileSpec, out _) ?? ValidateSpec(subtitleSpec, out _);
                if (specProblem != null) return Fail($"Tile '{title}': {specProblem}");

                if (string.IsNullOrEmpty(valueColumn)) return Fail($"Tile '{title}': choose the value column.");

                sourceType = "SPEC";
                sourceName = null;
            }
            else
            {
                sourceType = ReadString(element, "sourceType")?.ToUpperInvariant();
                sourceName = ReadString(element, "sourceName")?.Trim();

                var problem = CheckDetailSource(catalog, sourceType, sourceName, allowInflow: true, out var columns);
                if (problem != null) return Fail($"Tile '{title}': {problem}");

                if (string.IsNullOrEmpty(valueColumn) || !columns.Contains(valueColumn))
                    return Fail($"Tile '{title}': choose the value column.");

                if (!string.IsNullOrEmpty(subtitleColumn) && !columns.Contains(subtitleColumn))
                    return Fail($"Tile '{title}': the subtitle column is not in the source.");

                tileSpec = null;
                subtitleSpec = null;
            }

            tiles.Rows.Add(number, title, icon, accent, sourceType,
                           string.IsNullOrEmpty(sourceName) ? DBNull.Value : sourceName,
                           valueColumn,
                           string.IsNullOrEmpty(subtitleColumn) ? DBNull.Value : subtitleColumn,
                           format,
                           string.IsNullOrWhiteSpace(tileSpec) ? DBNull.Value : tileSpec,
                           string.IsNullOrWhiteSpace(subtitleSpec) ? DBNull.Value : subtitleSpec);
        }

        if (tiles.Rows.Count > 8)
            return Fail("Use at most 8 tiles so they fit on one row.");

        // --- tabs -------------------------------------------------------------------
        var tabs = ApsDb.NewDetailLayoutTabList();

        foreach (var element in ParseArray(tabsJson))
        {
            var number = tabs.Rows.Count + 1;
            var label = ReadString(element, "label")?.Trim();
            if (string.IsNullOrEmpty(label) || label.Length > 100)
                return Fail($"Tab {number}: enter a label of up to 100 characters.");

            var icon = ReadString(element, "icon");
            if (!ApsMetadata.DetailIcons.Contains(icon))
                return Fail($"Tab '{label}': choose an icon from the list.");

            var displayType = ReadString(element, "displayType")?.ToUpperInvariant();
            var settings = ReadObject(element, "settings");

            string settingsJson;
            string problem = displayType switch
            {
                "TABLE" => NormalizeTableSettings(catalog, settings, out settingsJson),
                "SUMMARY" => NormalizeSummarySettings(catalog, settings, out settingsJson),
                "CHECKLIST" => NormalizeChecklistSettings(catalog, settings, out settingsJson),
                "DOCVIEWER" => NormalizeDocViewerSettings(catalog, settings, out settingsJson),
                "NOTES" => NormalizeNotesSettings(catalog, settings, out settingsJson),
                _ => Unknown(out settingsJson)
            };

            if (problem != null) return Fail($"Tab '{label}': {problem}");

            tabs.Rows.Add(number, label, icon, displayType, settingsJson);
        }

        if (tabs.Rows.Count == 0)
            return Fail("Add at least one tab.");

        if (tabs.Rows.Count > 12)
            return Fail("Use at most 12 tabs.");

        var outputs = Db.ExecuteWithOutput("APS_DetailLayout_Save", p =>
        {
            ApsDb.In(p, "@UseCaseID", useCaseId);
            ApsDb.In(p, "@WorkspaceMode", workspaceMode);
            ApsDb.In(p, "@TileSourceSpecJson", string.IsNullOrWhiteSpace(sharedSpec) ? null : sharedSpec);
            ApsDb.Table(p, "@Tiles", "dbo.APS_DetailLayoutTileType", tiles);
            ApsDb.Table(p, "@Tabs", "dbo.APS_DetailLayoutTabType", tabs);
            ApsDb.In(p, "@ActionBy", ActionBy);
            ApsDb.Out(p, "@Success", SqlDbType.TinyInt);
            ApsDb.Out(p, "@Message", SqlDbType.VarChar, 200);
        });

        return FromProcedure(outputs);

        static string Unknown(out string json)
        {
            json = null;
            return "choose a display type.";
        }
    }


    /// <summary>Which checklist questions this use case asks.</summary>
    [HttpPost]
    public IActionResult SaveUseCaseQuestions(int useCaseId, string questionIds)
    {
        if (useCaseId <= 0) return Fail("Select a client and process first.");

        var ids = (questionIds ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => int.TryParse(x.Trim(), out var id) ? id : 0)
            .Where(id => id > 0)
            .Distinct()
            .ToList();

        var outputs = Db.ExecuteWithOutput("APS_ChecklistQuestionUseCase_Save", p =>
        {
            ApsDb.In(p, "@UseCaseID", useCaseId);
            ApsDb.In(p, "@QuestionIds", string.Join(",", ids));
            ApsDb.In(p, "@ActionBy", ActionBy);
            ApsDb.Out(p, "@Success", SqlDbType.TinyInt);
            ApsDb.Out(p, "@Message", SqlDbType.VarChar, 200);
        });

        return FromProcedure(outputs);
    }
    private static string CheckDetailSource(DetailCatalog catalog, string sourceType, string sourceName,
                                            bool allowInflow, out HashSet<string> columns)
    {
        columns = null;

        if (sourceType == "INFLOW")
        {
            if (!allowInflow) return "choose a table or stored procedure.";
            columns = catalog.InflowFields;
            return columns.Count == 0 ? "this use case has no active template yet." : null;
        }

        if (sourceType is not ("TABLE" or "PROC"))
            return "choose a source.";

        if (string.IsNullOrWhiteSpace(sourceName))
            return sourceType == "TABLE" ? "choose a table." : "choose a stored procedure.";

        columns = catalog.ColumnsOf(sourceType, sourceName);
        return columns == null
            ? $"'{sourceName}' is not an available {(sourceType == "TABLE" ? "table" : "stored procedure")}."
            : null;
    }

    private static readonly string[] SpecKinds = { "TABLE", "JOIN", "PROC" };
    private static readonly Regex SpecName = new("^[A-Za-z][A-Za-z0-9_]{0,127}$");
    private static readonly Regex SpecAlias = new("^[a-z][a-z0-9]{0,9}$");

    /// <summary>
    /// Shape only: SQL re-checks every table and column against the catalogue when
    /// the source is read, so this is a helpful message rather than the guard.
    /// </summary>
    private static string ValidateSpec(string specJson, out string cleaned)
    {
        cleaned = null;
        if (string.IsNullOrWhiteSpace(specJson)) return null;

        JsonElement spec;
        try
        {
            using var document = JsonDocument.Parse(specJson);
            spec = document.RootElement.Clone();
        }
        catch (JsonException) { return "the data source could not be read."; }

        var kind = (ReadString(spec, "kind") ?? string.Empty).ToUpperInvariant();
        if (!SpecKinds.Contains(kind)) return "choose a data source type.";

        if (kind == "PROC")
        {
            var procedure = ReadString(spec, "procedure");
            if (string.IsNullOrEmpty(procedure) || !SpecName.IsMatch(procedure))
                return "choose the stored procedure.";
        }
        else
        {
            var from = ReadObject(spec, "from");
            if (!SpecName.IsMatch(ReadString(from, "table") ?? string.Empty)) return "choose the main table.";
            if (!SpecAlias.IsMatch(ReadString(from, "alias") ?? string.Empty)) return "the main table needs an alias.";

            var aliases = new HashSet<string> { ReadString(from, "alias") };

            foreach (var join in ReadArray(spec, "joins"))
            {
                var alias = ReadString(join, "alias");
                if (!SpecName.IsMatch(ReadString(join, "table") ?? string.Empty)) return "a joined table is not valid.";
                if (!SpecAlias.IsMatch(alias ?? string.Empty)) return "each joined table needs an alias.";
                if (!aliases.Add(alias)) return $"the alias '{alias}' is used twice.";

                var type = (ReadString(join, "type") ?? "LEFT").ToUpperInvariant();
                if (type is not ("INNER" or "LEFT")) return "a join must be INNER or LEFT.";
                if (!ReadArray(join, "on").Any()) return $"the join to '{alias}' needs a condition.";
            }

            if (aliases.Count > 6) return "use at most six tables in one source.";
            if (!ReadArray(spec, "columns").Any()) return "choose at least one column.";

            var filters = ReadObject(spec, "filters");
            if (string.IsNullOrWhiteSpace(ReadString(filters, "inflowDataId")) &&
                string.IsNullOrWhiteSpace(ReadString(filters, "useCaseId")))
                return "the source must be filtered by the record or the use case.";
        }

        var jsonColumn = ReadString(spec, "jsonColumn");
        if (!string.IsNullOrEmpty(jsonColumn) && !SpecName.IsMatch(jsonColumn))
            return "the JSON column name is not valid.";

        cleaned = specJson;
        return null;
    }

    private static string NormalizeTableSettings(DetailCatalog catalog, JsonElement s, out string json)
    {
        json = null;

        var specJson = ReadString(s, "specJson");
        var sourceType = ReadString(s, "sourceType")?.ToUpperInvariant();
        var sourceName = ReadString(s, "sourceName")?.Trim();
        HashSet<string> available;

        if (!string.IsNullOrWhiteSpace(specJson))
        {
            var specProblem = ValidateSpec(specJson, out _);
            if (specProblem != null) return specProblem;
            available = null;                      // columns are checked by SQL when read
        }
        else
        {
            var problem = CheckDetailSource(catalog, sourceType, sourceName, allowInflow: true, out available);
            if (problem != null) return problem;
        }

        var columns = new List<object>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var c in ReadArray(s, "columns"))
        {
            var column = ReadString(c, "column");
            if (string.IsNullOrEmpty(column)) continue;
            if (available != null && !available.Contains(column)) return $"'{column}' is not a column of {sourceName}.";
            if (!seen.Add(column)) return $"'{column}' is listed more than once.";

            var header = ReadString(c, "header")?.Trim();
            if (string.IsNullOrEmpty(header)) header = column;
            if (header.Length > 100) return $"the display name for '{column}' is longer than 100 characters.";

            columns.Add(new { column, header });
        }

        if (columns.Count == 0) return "choose at least one column to show.";

        var pageSize = ReadInt(s, "pageSize") ?? 25;
        if (pageSize is < 5 or > 200) return "rows per page must be between 5 and 200.";

        json = JsonSerializer.Serialize(new { sourceType, sourceName, specJson, pageSize, columns });
        return null;
    }

    private static string NormalizeSummarySettings(DetailCatalog catalog, JsonElement s, out string json)
    {
        json = null;

        var specJson = ReadString(s, "specJson");
        var sourceType = ReadString(s, "sourceType")?.ToUpperInvariant();
        var sourceName = ReadString(s, "sourceName")?.Trim();
        HashSet<string> available;

        if (!string.IsNullOrWhiteSpace(specJson))
        {
            var specProblem = ValidateSpec(specJson, out _);
            if (specProblem != null) return specProblem;
            available = null;                      // columns are checked by SQL when read
        }
        else
        {
            var problem = CheckDetailSource(catalog, sourceType, sourceName, allowInflow: true, out available);
            if (problem != null) return problem;
        }

        var cards = new List<object>();

        foreach (var c in ReadArray(s, "cards"))
        {
            var title = ReadString(c, "title")?.Trim();
            var column = ReadString(c, "column");
            if (string.IsNullOrEmpty(title) && string.IsNullOrEmpty(column)) continue;

            if (string.IsNullOrEmpty(title) || title.Length > 100)
                return "every card needs a title of up to 100 characters.";
            if (string.IsNullOrEmpty(column) || (available != null && !available.Contains(column)))
                return $"card '{title}': choose its column.";

            var icon = ReadString(c, "icon");
            if (!ApsMetadata.DetailIcons.Contains(icon)) return $"card '{title}': choose an icon.";

            var accent = ReadString(c, "accent");
            if (!ApsMetadata.DetailAccents.Contains(accent)) return $"card '{title}': choose an accent colour.";

            var format = ReadString(c, "format") ?? "text";
            if (!ApsMetadata.DetailValueFormats.Contains(format)) return $"card '{title}': choose a format.";

            cards.Add(new { title, icon, accent, column, format });
        }

        if (cards.Count == 0) return "add at least one card.";

        json = JsonSerializer.Serialize(new
        {
            sourceType,
            sourceName = sourceType == "INFLOW" ? null : sourceName,
            specJson,
            cards
        });
        return null;
    }

    private static string NormalizeChecklistSettings(DetailCatalog catalog, JsonElement s, out string json)
    {
        json = null;

        // An empty list means every question mapped to the use case.
        var questionIds = new List<int>();
        foreach (var q in ReadArray(s, "questionIds"))
            if (q.ValueKind == JsonValueKind.Number && q.TryGetInt32(out var id) && !questionIds.Contains(id))
                questionIds.Add(id);

        var evidence = ReadObject(s, "evidence");
        var specJson = ReadString(evidence, "specJson");
        object evidenceSource = null;
        var evidenceColumns = new List<object>();

        if (!string.IsNullOrWhiteSpace(specJson))
        {
            var specProblem = ValidateSpec(specJson, out _);
            if (specProblem != null) return $"evidence source: {specProblem}";

            var questionColumn = ReadString(evidence, "questionColumn");
            if (string.IsNullOrWhiteSpace(questionColumn))
                return "choose the column that holds the question ID.";

            evidenceSource = new
            {
                specJson,
                questionColumn,
                resultColumn = ReadString(evidence, "resultColumn"),
                justificationColumn = ReadString(evidence, "justificationColumn")
            };

            foreach (var c in ReadArray(s, "evidenceColumns"))
            {
                var column = ReadString(c, "column");
                if (string.IsNullOrEmpty(column)) continue;

                var header = ReadString(c, "header")?.Trim();
                if (string.IsNullOrEmpty(header)) header = column;
                if (header.Length > 100) return $"the label for '{column}' is longer than 100 characters.";

                evidenceColumns.Add(new { column, header });
            }
        }

        // The older procedure-based shape still saves, so existing tabs keep working.
        var rs = ReadObject(s, "resultSource");
        var procedure = ReadString(rs, "procedure")?.Trim();
        object resultSource = null;

        if (evidenceSource == null && !string.IsNullOrEmpty(procedure))
        {
            var columns = catalog.ColumnsOf("PROC", procedure);
            if (columns == null) return $"'{procedure}' is not an available stored procedure.";

            var questionColumn = ReadString(rs, "questionColumn");
            var resultColumn = ReadString(rs, "resultColumn");
            var commentColumn = ReadString(rs, "commentColumn");

            if (string.IsNullOrEmpty(questionColumn) || !columns.Contains(questionColumn))
                return "choose the result column that holds the question ID.";
            if (string.IsNullOrEmpty(resultColumn) || !columns.Contains(resultColumn))
                return "choose the result column that holds Pass / Fail.";
            if (!string.IsNullOrEmpty(commentColumn) && !columns.Contains(commentColumn))
                return "the comment column is not returned by the procedure.";

            resultSource = new { procedure, questionColumn, resultColumn, commentColumn = string.IsNullOrEmpty(commentColumn) ? null : commentColumn };
        }

        var colors = new List<object>();
        var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var c in ReadArray(s, "colors"))
        {
            var value = ReadString(c, "value")?.Trim();
            if (string.IsNullOrEmpty(value)) continue;
            if (value.Length > 50) return $"the result value '{value}' is longer than 50 characters.";
            if (!values.Add(value)) return $"the result '{value}' has more than one colour rule.";

            var color = ReadString(c, "color");
            if (!ApsMetadata.DetailStatusColors.Contains(color)) return $"choose a colour for '{value}'.";

            colors.Add(new { value, color });
        }

        json = JsonSerializer.Serialize(new { questionIds, evidence = evidenceSource, evidenceColumns, resultSource, colors });
        return null;
    }

    private static string NormalizeNotesSettings(DetailCatalog catalog, JsonElement s, out string json)
    {
        json = null;

        object Feed(string name, out string problem)
        {
            problem = null;
            var feed = ReadObject(s, name);
            var specJson = ReadString(feed, "specJson");

            if (string.IsNullOrWhiteSpace(specJson)) return null;

            problem = ValidateSpec(specJson, out _);
            if (problem != null) { problem = $"{name} notes: {problem}"; return null; }

            var contentColumn = ReadString(feed, "contentColumn");
            if (string.IsNullOrWhiteSpace(contentColumn))
            {
                problem = $"{name} notes: choose the column holding the note text.";
                return null;
            }

            var order = (ReadString(feed, "order") ?? "desc").ToLowerInvariant();
            if (order is not ("asc" or "desc")) order = "desc";

            return new
            {
                specJson,
                sourceColumn = ReadString(feed, "sourceColumn"),
                dateColumn = ReadString(feed, "dateColumn"),
                contentColumn,
                order
            };
        }

        var live = Feed("live", out var liveProblem);
        if (liveProblem != null) return liveProblem;

        var summary = Feed("summary", out var summaryProblem);
        if (summaryProblem != null) return summaryProblem;

        if (live == null && summary == null) return "set up the live notes, the AI summary, or both.";

        var defaultView = (ReadString(s, "defaultView") ?? "live").ToLowerInvariant();
        if (defaultView is not ("live" or "summary")) defaultView = "live";
        if (live == null) defaultView = "summary";

        var refreshPipelineId = ReadInt(s, "refreshPipelineId") ?? 0;

        json = JsonSerializer.Serialize(new { live, summary, defaultView, refreshPipelineId });
        return null;
    }

    private static string NormalizeDocViewerSettings(DetailCatalog catalog, JsonElement s, out string json)
    {
        json = null;

        var procedure = ReadString(s, "procedure")?.Trim();
        if (string.IsNullOrEmpty(procedure)) return "choose the document stored procedure.";

        var columns = catalog.ColumnsOf("PROC", procedure);
        if (columns == null) return $"'{procedure}' is not an available stored procedure.";

        var mapElement = ReadObject(s, "map");
        var map = new Dictionary<string, string>();

        foreach (var (key, label) in DocViewerMap)
        {
            var column = ReadString(mapElement, key);
            if (string.IsNullOrEmpty(column) || !columns.Contains(column))
                return $"map the {label} column.";
            map[key] = column;
        }

        // Optional: page number, field order and which field holds the page summary.
        foreach (var (key, label) in new[] { ("pageNo", "page number"), ("fieldOrder", "field order"), ("fieldKey", "row key") })
        {
            var optional = ReadString(mapElement, key);
            if (string.IsNullOrEmpty(optional)) continue;
            if (!columns.Contains(optional)) return $"the {label} column is not returned by the procedure.";
            map[key] = optional;
        }

        var summaryField = ReadString(s, "summaryField")?.Trim();
        if (summaryField?.Length > 100) return "the summary field name is longer than 100 characters.";
        if (string.IsNullOrEmpty(summaryField)) summaryField = "Summary";

        var sections = new List<object>();
        var seen = new HashSet<string>();
        var total = 0;

        foreach (var section in ReadArray(s, "sections"))
        {
            var key = ReadString(section, "key");
            if (!DocViewerSections.Contains(key) || !seen.Add(key))
                return "the document viewer sections are not valid.";

            var width = ReadInt(section, "width") ?? 0;
            if (width is < 2 or > 8) return "each section width must be between 2 and 8 columns.";

            total += width;
            sections.Add(new { key, width });
        }

        if (seen.Count != DocViewerSections.Length) return "the document viewer needs all three sections.";
        if (total != 12) return $"section widths must add up to 12 (currently {total}).";

        // Editing writes to a real table, so the target is named here and
        // re-checked against the catalogue when a value is saved.
        var editElement = ReadObject(s, "edit");
        object edit = null;

        if (editElement.ValueKind == JsonValueKind.Object &&
            ReadString(editElement, "enabled") is "true" or "True")
        {
            var table = ReadString(editElement, "table")?.Trim();
            var keyColumn = ReadString(editElement, "keyColumn")?.Trim();
            var valueColumn = ReadString(editElement, "valueColumn")?.Trim();

            if (string.IsNullOrEmpty(table) || string.IsNullOrEmpty(keyColumn) || string.IsNullOrEmpty(valueColumn))
                return "to allow editing, choose the table, its key column and the value column.";

            if (!map.ContainsKey("fieldKey"))
                return "to allow editing, map the row key column from the procedure.";

            edit = new { enabled = true, table, keyColumn, valueColumn };
        }

        json = JsonSerializer.Serialize(new { procedure, map, sections, summaryField, edit });
        return null;
    }

    private static JsonElement ReadObject(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.Object
            ? value
            : default;

    private static IEnumerable<JsonElement> ReadArray(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(property, out var value) ||
            value.ValueKind != JsonValueKind.Array)
            return Array.Empty<JsonElement>();

        return value.EnumerateArray().ToList();
    }

    // =========================================================================
    // Step 4 - Detail layout
    // =========================================================================

    [HttpPost]
    public IActionResult SaveDetailConfig(byte mode, bool knowledgeai, bool allowapprove,
                                          bool allowreject, bool allowclarify, bool allowcomments,
                                          string layoutJson)
    {
        JsonElement layout;
        try
        {
            layout = JsonDocument.Parse(layoutJson ?? "{}").RootElement;
        }
        catch (JsonException)
        {
            return Fail("The detail layout could not be read.");
        }
        var useCases = LoadUseCases();
        var allowedUseCaseIds = useCases.AsEnumerable()
            .Select(r => ToInt(r, "UseCaseID"))
            .Where(id => id > 0)
            .ToHashSet();



        var useCaseId = allowedUseCaseIds.First();
        var tabs = ApsDb.NewDetailTabList();
        var fields = ApsDb.NewDetailFieldList();
        var catalog = Meta.InflowColumns(useCaseId).AsEnumerable()
            .Select(r => Convert.ToString(r["columnname"]))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        short rowKey = 0;

        // Header fields carry a null rowkey, which the procedure reads as
        // "header scope" rather than "belongs to a tab".
        if (layout.TryGetProperty("headerFields", out var headerFields) &&
            headerFields.ValueKind == JsonValueKind.Array)
        {
            short order = 0;
            foreach (var field in headerFields.EnumerateArray())
            {
                var name = ReadString(field, "name");
                if (string.IsNullOrWhiteSpace(name)) continue;
                if (!catalog.Contains(name))
                    return Fail($"Header field '{name}' is not part of the current inflow template.");

                fields.Rows.Add(DBNull.Value, mode, name, ReadBool(field, "required"), ++order);
            }
        }

        if (fields.Rows.Count == 0)
            return Fail("Configure at least one header field.");

        if (!layout.TryGetProperty("tabs", out var tabArray) || tabArray.ValueKind != JsonValueKind.Array)
            return Fail("Configure at least one primary tab.");

        foreach (var tab in tabArray.EnumerateArray())
        {
            var label = ReadString(tab, "label");
            if (string.IsNullOrWhiteSpace(label))
                return Fail("Every tab needs a label.");

            var parentKey = ++rowKey;
            tabs.Rows.Add(parentKey, DBNull.Value, mode,
                          SlugFor(tab, label, parentKey), label.Trim(),
                          ReadString(tab, "icon"),
                          Coalesce(ReadString(tab, "displayType"), "table"),
                          ReadShort(tab, "badge"),
                          parentKey);

            AddFields(tab, parentKey, label);

            if (tab.TryGetProperty("subtabs", out var subtabs) &&
                subtabs.ValueKind == JsonValueKind.Array)
            {
                short subOrder = 0;
                foreach (var subtab in subtabs.EnumerateArray())
                {
                    var subLabel = ReadString(subtab, "label");
                    if (string.IsNullOrWhiteSpace(subLabel))
                        return Fail($"A sub-tab under '{label}' is missing its label.");

                    var subKey = ++rowKey;
                    tabs.Rows.Add(subKey, parentKey, mode,
                                  SlugFor(subtab, subLabel, subKey), subLabel.Trim(),
                                  ReadString(subtab, "icon"),
                                  Coalesce(ReadString(subtab, "displayType"), "table"),
                                  ReadShort(subtab, "badge"),
                                  ++subOrder);

                    AddFields(subtab, subKey, subLabel);
                }
            }
        }

        if (tabs.Rows.Count == 0)
            return Fail("Configure at least one primary tab.");

        var outputs = Db.ExecuteWithOutput("APS_DetailConfig_Save", p =>
        {
            ApsDb.In(p, "@mode", mode);
            ApsDb.In(p, "@knowledgeai", knowledgeai);
            ApsDb.In(p, "@allowapprove", allowapprove);
            ApsDb.In(p, "@allowreject", allowreject);
            ApsDb.In(p, "@allowclarify", allowclarify);
            ApsDb.In(p, "@allowcomments", allowcomments);
            ApsDb.Table(p, "@tabs", "dbo.APS_DetailTabType", tabs);
            ApsDb.Table(p, "@fields", "dbo.APS_DetailFieldType", fields);
            ApsDb.In(p, "@actionby", ActionBy);
            ApsDb.Out(p, "@success", SqlDbType.Bit);
            ApsDb.Out(p, "@message", SqlDbType.NVarChar, 400);
        });

        return FromProcedure(outputs);

        void AddFields(JsonElement source, short key, string ownerLabel)
        {
            if (!source.TryGetProperty("fields", out var fieldArray) ||
                fieldArray.ValueKind != JsonValueKind.Array) return;

            short order = 0;
            foreach (var field in fieldArray.EnumerateArray())
            {
                var name = ReadString(field, "name");
                if (string.IsNullOrWhiteSpace(name) || !catalog.Contains(name)) continue;
                fields.Rows.Add(key, mode, name, ReadBool(field, "required"), ++order);
            }
        }
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static IEnumerable<JsonElement> ParseArray(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) yield break;

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            yield break;
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Array) yield break;

            foreach (var element in document.RootElement.EnumerateArray())
            {
                yield return element.Clone();
            }
        }
    }

    private static string ReadString(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        if (!element.TryGetProperty(property, out var value)) return null;

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.ToString(),
            _ => null
        };
    }

    private static bool ReadBool(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object) return false;
        if (!element.TryGetProperty(property, out var value)) return false;
        return value.ValueKind == JsonValueKind.True;
    }

    private static object ReadShort(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object) return DBNull.Value;
        if (!element.TryGetProperty(property, out var value)) return DBNull.Value;
        if (value.ValueKind != JsonValueKind.Number) return DBNull.Value;
        return value.TryGetInt16(out var result) ? result : DBNull.Value;
    }

    private static string Coalesce(string value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;

    /// <summary>Stable key for a tab; falls back to a slug of the label.</summary>
    private static string SlugFor(JsonElement element, string label, short rowKey)
    {
        var key = ReadString(element, "key");
        if (!string.IsNullOrWhiteSpace(key)) return Truncate(key.Trim(), 60);

        var builder = new StringBuilder();
        foreach (var character in label.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character)) builder.Append(character);
            else if (builder.Length > 0 && builder[^1] != '-') builder.Append('-');
        }

        var slug = builder.ToString().Trim('-');
        return Truncate(slug.Length == 0 ? $"tab-{rowKey}" : $"{slug}-{rowKey}", 60);
    }

    private static string Truncate(string value, int length) =>
        value.Length <= length ? value : value[..length];

    /// <summary>Matches the identifier rule enforced inside the procedures.</summary>
    internal static bool IsValidIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128) return false;
        if (!char.IsLetter(value[0])) return false;
        return value.All(c => char.IsLetterOrDigit(c) || c == '_');
    }

    internal static bool IsValidCollectionName(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 100) return false;
        if (!char.IsLetter(value[0])) return false;
        return value.All(char.IsLetterOrDigit);
    }

    private static bool IsParameter(string value)
    {
        var name = value?.TrimStart('@');
        return !string.IsNullOrWhiteSpace(name) && IsValidIdentifier(name);
    }
}

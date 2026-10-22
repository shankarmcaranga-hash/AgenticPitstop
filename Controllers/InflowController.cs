using System.Data;
using System.Data;
using System.Text.Json;
using AgenticPitstop.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace AgenticPitstop.Controllers;

[ApsAuthorize]
public sealed class InflowController : ApsController
{
    private const long MaxUploadBytes = 10 * 1024 * 1024;
    private const int MaxRows = 10000;

    private readonly InflowArchiveService _archive;

    public InflowController(ApsDb db, ApsIdentity identity, ApsMetadata meta,
                            InflowArchiveService archive)
        : base(db, identity, meta)
    {
        _archive = archive;
    }

    protected override string ActiveScreenKey => "inflow";

    [HttpGet]
    public IActionResult Index(int useCaseId = 0)
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

        if (useCaseId <= 0)
        {
            ViewBag.Config = null;
            ViewBag.TemplateColumns = new DataTable();
            ViewBag.History = new DataTable();
            return View();
        }

        var configSet = Db.QueryMultiple("APS_InflowConfig_GetActive", p => ApsDb.In(p, "@UseCaseID", useCaseId));
        ViewBag.Config = configSet.Tables.Count > 0 && configSet.Tables[0].Rows.Count > 0 ? configSet.Tables[0].Rows[0] : null;
        ViewBag.TemplateColumns = configSet.Tables.Count > 1 ? configSet.Tables[1] : new DataTable();
        ViewBag.History = Db.Query("APS_Inflow_GetHistory", p =>
        {
            ApsDb.In(p, "@UseCaseID", useCaseId);
            ApsDb.In(p, "@Top", 20);
        });

        return View();
    }

    private const int PreviewRowLimit = 200;

    /// <summary>Reads and validates the workbook without saving anything.</summary>
    [HttpPost]
    [RequestSizeLimit(MaxUploadBytes)]
    public async Task<IActionResult> PreviewUpload(IFormFile file, int useCaseId)
    {
        var parsed = await ParseUploadAsync(file, useCaseId);
        if (parsed.Error != null) return Fail(parsed.Error);

        return Ok($"{parsed.TotalCount} row(s) read. Review them, then click Save to import.", new
        {
            Columns = parsed.Columns.Select(c => new { FieldCode = c.FieldCode, Header = c.Header }),
            Rows = parsed.PreviewRows,
            parsed.TotalCount,
            parsed.ValidCount,
            parsed.InvalidCount,
            IgnoredExtraColumns = parsed.ExtraColumns,
            PreviewLimit = PreviewRowLimit
        });
    }

    /// <summary>Validates the same file again on the server, then stores it.</summary>
    [HttpPost]
    [RequestSizeLimit(MaxUploadBytes)]
    public async Task<IActionResult> Upload(IFormFile file, int useCaseId)
    {
        var parsed = await ParseUploadAsync(file, useCaseId);
        if (parsed.Error != null) return Fail(parsed.Error);

        // The workbook is archived before its rows are saved, and a failure here
        // stops the import. A use case that is configured to keep every uploaded
        // file must not end up with imported data and no file to audit it
        // against; the user retries instead.
        //
        // A use case with no storage configured archives nothing and this is a
        // no-op, so every existing use case behaves exactly as before.
        InflowArchiveResult archived;
        try
        {
            var archiveRow = Db.QuerySingle("APS_InflowConfig_GetArchive",
                p => ApsDb.In(p, "@UseCaseID", useCaseId));

            // DBNull must become null rather than "", or Parse quietly returns
            // the default settings and archiving turns itself off with no trace.
            var archiveJson = archiveRow == null || archiveRow["ArchiveJson"] == DBNull.Value
                ? null
                : Convert.ToString(archiveRow["ArchiveJson"]);

            await using var original = file.OpenReadStream();

            archived = await _archive.ArchiveAsync(
                archiveJson, useCaseId, original, file.FileName,
                file.ContentType, HttpContext.RequestAborted);
        }
        catch (Exception ex)
        {
            return Fail($"The file could not be stored, so nothing was imported: {ex.Message}");
        }

        var outputs = Db.ExecuteWithOutput("APS_Inflow_Save", p =>
        {
            ApsDb.In(p, "@UseCaseID", useCaseId);      
            ApsDb.In(p, "@InflowConfigID", parsed.InflowConfigId);
            ApsDb.In(p, "@OriginalFileName", Path.GetFileName(file.FileName));
            // Where the copy was kept, so a batch can be traced back to its file.
            // Null when this use case keeps no copy.
            ApsDb.In(p, "@StoredFileName", archived?.Location);
            ApsDb.In(p, "@TotalRowCount", parsed.TotalCount);
            ApsDb.In(p, "@ValidRowCount", parsed.ValidCount);
            ApsDb.In(p, "@InvalidRowCount", parsed.InvalidCount);
            ApsDb.Table(p, "@Rows", "dbo.APS_InflowDataType", parsed.Rows);
            ApsDb.In(p, "@UploadedBy", ActionBy);
            ApsDb.Out(p, "@InflowID", SqlDbType.Int);
            ApsDb.Out(p, "@Message", SqlDbType.VarChar, 200);
        });

        var resultData = new
        {
            ImportedCount = parsed.ValidCount,
            RejectedCount = parsed.InvalidCount,
           
            TotalCount = parsed.TotalCount,
            StoredIn = archived?.Provider,
            StoredAt = archived?.Location
        };

        

        return FromProcedure(outputs, "InflowID", resultData);
    }

    private sealed class ParsedUpload
    {
        public string Error { get; set; }
        public int InflowConfigId { get; set; }
        public DataTable Rows { get; set; }
        public List<(string FieldCode, string Header)> Columns { get; } = new();
        public List<object> PreviewRows { get; } = new();
        public List<string> ExtraColumns { get; set; } = new();
        public int TotalCount { get; set; }
        public int ValidCount { get; set; }
        public int InvalidCount { get; set; }
    }

    private static ParsedUpload Rejected(string message) => new() { Error = message };

    /// <summary>Shared by preview and save, so both apply exactly the same rules.</summary>
    private async Task<ParsedUpload> ParseUploadAsync(IFormFile file, int useCaseId)
    {
        if (!IsAllowedUseCase(useCaseId))
            return Rejected("You do not have access to this use case.");

        if (file == null || file.Length == 0)
            return Rejected("Choose an Excel file to upload.");

        if (!Path.GetExtension(file.FileName).Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
            return Rejected("Only .xlsx workbooks are supported in this version.");

        if (file.Length > MaxUploadBytes)
            return Rejected("The file is larger than the 10 MB upload limit.");

        var activeConfig = Db.QueryMultiple("APS_InflowConfig_GetActive", p => ApsDb.In(p, "@UseCaseID", useCaseId));
        var config = activeConfig.Tables.Count > 0 && activeConfig.Tables[0].Rows.Count > 0
            ? activeConfig.Tables[0].Rows[0]
            : null;
        var templateColumns = activeConfig.Tables.Count > 1 ? activeConfig.Tables[1] : new DataTable();

        if (config == null || templateColumns.Rows.Count == 0)
            return Rejected("No inflow template is configured yet. An Admin needs to upload one first.");

        var inflowConfigId = ToInt(config, "InflowConfigID");
        if (inflowConfigId <= 0)
            return Rejected("No active template is available for the selected use case.");

        var mappings = templateColumns.AsEnumerable()
            .OrderBy(r => ToInt(r, "SortOrder"))
            .Select(r => (Header: ToStringValue(r, "ExcelColumnName"),
                          FieldCode: ToStringValue(r, "FieldCode"),
                          Required: ToBool(r, "IsRequired")))
            .Where(m => !string.IsNullOrWhiteSpace(m.Header) && !string.IsNullOrWhiteSpace(m.FieldCode))
            .ToList();

        var result = new ParsedUpload { InflowConfigId = inflowConfigId, Rows = NewInflowRowsTable() };
        foreach (var m in mappings) result.Columns.Add((m.FieldCode, m.Header));

        DataTable sheet;
        try
        {
            using var stream = new MemoryStream();
            await file.CopyToAsync(stream);
            stream.Position = 0;
            sheet = ApsExcel.Read(stream, MaxRows);
        }
        catch (InvalidDataException)
        {
            return Rejected("That file could not be read as an .xlsx workbook.");
        }

        if (sheet.Rows.Count == 0)
            return Rejected("The workbook has a header row but no data rows.");

        var fileColumns = sheet.Columns.Cast<DataColumn>().Select(c => c.ColumnName).ToList();
        var expected = mappings.Select(m => m.Header).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        // Headers are matched with their spaces removed, because the template
        // stores "PolicyNumber" for a workbook whose header reads "Policy Number".
        // For a template with no spaces this is identical to the old comparison.
        var fileBySquashed = new Dictionary<string, DataColumn>(StringComparer.OrdinalIgnoreCase);
        foreach (DataColumn column in sheet.Columns)
            fileBySquashed.TryAdd(Squash(column.ColumnName), column);

        var missing = expected.Where(h => !fileBySquashed.ContainsKey(Squash(h))).ToList();
        if (missing.Count > 0)
            return Rejected($"The file is missing {missing.Count} template column(s): {string.Join(", ", missing)}.");

        var expectedSquashed = new HashSet<string>(expected.Select(Squash), StringComparer.OrdinalIgnoreCase);
        result.ExtraColumns = fileColumns
            .Where(f => !expectedSquashed.Contains(Squash(f)))
            .ToList();

        var sourceColumns = mappings
            .Select(m => fileBySquashed[Squash(m.Header)])
            .ToList();

        foreach (DataRow sourceRow in sheet.Rows)
        {
            var rowData = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var missingRequired = new List<string>();
            var hasAnyValue = false;

            for (var i = 0; i < mappings.Count; i++)
            {
                var value = Convert.ToString(sourceRow[sourceColumns[i]])?.Trim() ?? string.Empty;
                if (value.Length > 0) hasAnyValue = true;

                rowData[mappings[i].FieldCode] = value;

                if (mappings[i].Required && value.Length == 0)
                    missingRequired.Add(mappings[i].Header);
            }

            if (!hasAnyValue) continue;

            result.TotalCount++;

            var rowNumber = ToInt(sourceRow, "RowNumber");
            if (rowNumber <= 0) rowNumber = result.TotalCount + 1;

            var isValid = missingRequired.Count == 0;
            var message = isValid ? string.Empty : $"Missing required columns: {string.Join(", ", missingRequired)}";

            var row = result.Rows.NewRow();
            row["RowNumber"] = rowNumber;
            row["RowDataJson"] = JsonSerializer.Serialize(rowData);
            row["ValidationStatusCode"] = isValid ? "VALID" : "INVALID";
            row["ValidationMessage"] = message;
            row["IsEligible"] = isValid ? (byte)1 : (byte)0;
            result.Rows.Rows.Add(row);

            if (isValid) result.ValidCount++; else result.InvalidCount++;

            if (result.PreviewRows.Count < PreviewRowLimit)
            {
                result.PreviewRows.Add(new
                {
                    RowNumber = rowNumber,
                    IsValid = isValid,
                    Message = message,
                    Values = rowData
                });
            }
        }

        if (result.TotalCount == 0)
            return Rejected("The workbook has no rows with values under the template columns.");

        return result;
    }

    [HttpPost]
    public IActionResult BatchDetail(int inflowId)
    {
        var data = Db.QueryMultiple("APS_Inflow_GetById", p => ApsDb.In(p, "@InflowID", inflowId));
        var batch = data.Tables.Count > 0 && data.Tables[0].Rows.Count > 0 ? data.Tables[0].Rows[0] : null;
        var rejectedRows = data.Tables.Count > 1 ? data.Tables[1] : new DataTable();

        if (batch == null)
            return Fail("Batch not found.");

        return Ok("Batch loaded.", new
        {
            Batch = batch.Table.Columns.Cast<DataColumn>().ToDictionary(c => c.ColumnName, c => batch[c] == DBNull.Value ? null : batch[c]),
            RejectedRows = rejectedRows.AsEnumerable().Select(r => rejectedRows.Columns.Cast<DataColumn>()
                .ToDictionary(c => c.ColumnName, c => r[c] == DBNull.Value ? null : r[c])).ToList()
        });
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

        return outputs.TryGetValue("IsAllowed", out var allowed)
            && allowed != null
            && Convert.ToBoolean(allowed);
    }
    /// <summary>A header with its whitespace removed. Used for matching only.</summary>
    private static string Squash(string value) =>
        string.IsNullOrEmpty(value)
            ? string.Empty
            : new string(value.Where(c => !char.IsWhiteSpace(c)).ToArray());
    private static DataTable NewInflowRowsTable()
    {
        var table = new DataTable();
        table.Columns.Add("RowNumber", typeof(int));
        table.Columns.Add("RowDataJson", typeof(string));
        table.Columns.Add("ValidationStatusCode", typeof(string));
        table.Columns.Add("ValidationMessage", typeof(string));
        table.Columns.Add("IsEligible", typeof(byte));
        return table;
    }

    private static string ToStringValue(DataRow row, string column)
    {
        var actual = FindColumn(row.Table, column);
        if (actual == null || row[actual] == DBNull.Value) return string.Empty;
        return Convert.ToString(row[actual]) ?? string.Empty;
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

using AgenticPitstop.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using System.Data;
using Microsoft.Data.SqlClient;

namespace AgenticPitstop.Controllers;

/// <summary>
/// Dynamic Reports. Deliberately a structural placeholder: the filter shell and
/// the field catalogue are wired to real configuration, but the report definition
/// model is still open. Nothing here should be treated as final.
/// </summary>
[ApsAuthorize]
public sealed class ReportsController : ApsController
{
    public ReportsController(ApsDb db, ApsIdentity identity, ApsMetadata meta)
        : base(db, identity, meta) { }

    protected override string ActiveScreenKey => "reports";

    [HttpGet]
    public IActionResult Index()
    {
        ViewBag.ReportCatalog = Db.Query("APS_ReportMaster_Get");
        return View();
    }

    [HttpGet]
    public IActionResult Metadata(short reportId)
    {
        var configuration = Db.QueryMultiple("APS_Report_Configuration_Get",
            p => ApsDb.In(p, "@reportid", reportId));
        return Ok("Report metadata loaded.", new
        {
            definition = configuration.Tables.Count > 0 ? ApsMetadata.ToList(configuration.Tables[0]).FirstOrDefault() : null,
            parameters = configuration.Tables.Count > 1 ? ApsMetadata.ToList(configuration.Tables[1]) : new List<Dictionary<string, object>>(),
            columns = configuration.Tables.Count > 2 ? ApsMetadata.ToList(configuration.Tables[2]) : new List<Dictionary<string, object>>()
        });
    }

    [HttpGet]
    public IActionResult Lookup(short reportId, string parameterName)
    {
        var configuration = Db.QueryMultiple("APS_Report_Configuration_Get",
            p => ApsDb.In(p, "@reportid", reportId));
        if (configuration.Tables.Count < 2)
            return Fail("The selected report has no parameter configuration.");

        /*var parameter = configuration.Tables[1].AsEnumerable().FirstOrDefault(row =>
            string.Equals(ReadColumn(row, "parametername"), parameterName, StringComparison.OrdinalIgnoreCase));
        var procedure = parameter == null ? null : ReadColumn(parameter, "lookupprocedure");
        if (!IsIdentifier(procedure))
            return Fail("No approved lookup procedure is configured for this filter.");*/

        if (configuration.Tables.Count < 2)
            return Fail("Parameter configuration table not found.");

        var table = configuration.Tables[1];

        if (!table.Columns.Contains("parametername"))
            return Fail("parametername column missing.");

        if (!table.Columns.Contains("lookupprocedure"))
            return Fail("lookupprocedure column missing.");

        var parameter = table.AsEnumerable()
            .FirstOrDefault(row =>
                string.Equals(
                    ReadColumn(row, "displayname"),
                    parameterName,
                    StringComparison.OrdinalIgnoreCase));

        if (parameter == null)
            return Fail($"Parameter '{parameterName}' not configured.");

        var procedure = ReadColumn(parameter, "lookupprocedure");

        if (string.IsNullOrWhiteSpace(procedure) ||
            !IsIdentifier(procedure))
        {
            return Fail("No approved lookup procedure is configured for this filter.");
        }
         
        var values = Db.Query(procedure);
        return Ok("Lookup values loaded.", ApsMetadata.ToList(values));
    }

    [HttpPost]
    public IActionResult Run(short reportId)
    {
        var configuration = Db.QueryMultiple("APS_Report_Configuration_Get",
            p => ApsDb.In(p, "@reportid", reportId));

        if (configuration.Tables.Count < 2 || configuration.Tables[0].Rows.Count == 0)
            return Fail("The selected report is not configured.");

        var definition = configuration.Tables[0].Rows[0];
        var schema = Convert.ToString(definition["procedureschema"]);
        var procedure = Convert.ToString(definition["procedurename"]);
        if (!IsIdentifier(schema) || !IsIdentifier(procedure))
            return Fail("The report stored procedure mapping is invalid.");

        var result = Db.Query($"{schema}.{procedure}", parameters =>
        {
            foreach (DataRow parameter in configuration.Tables[1].Rows)
            {
                var name = Convert.ToString(parameter["parametername"]);
                if (!IsParameter(name)) continue;

                var key = name.TrimStart('@');
                var value = Request.Form[key].FirstOrDefault();
                parameters.AddWithValue("@" + key, string.IsNullOrWhiteSpace(value) ? DBNull.Value : value);
            }
        });

        return Ok("Report executed.", new
        {
            columns = result.Columns.Cast<DataColumn>().Select(c => c.ColumnName),
            rows = ApsMetadata.ToList(result).Take(5000).ToList(),
            maxRows = 5000
        });
    }

    private static bool IsIdentifier(string value) =>
        !string.IsNullOrWhiteSpace(value) && value.All(c => char.IsLetterOrDigit(c) || c == '_') && char.IsLetter(value[0]);

    private static bool IsParameter(string value) =>
        !string.IsNullOrWhiteSpace(value) && value.TrimStart('@').Length > 0 && IsIdentifier(value.TrimStart('@'));

    private static string ReadColumn(DataRow row, string name)
    {
        var column = row.Table.Columns.Cast<DataColumn>()
            .FirstOrDefault(candidate => string.Equals(candidate.ColumnName, name, StringComparison.OrdinalIgnoreCase));
        return column == null ? null : Convert.ToString(row[column]);
    }
}

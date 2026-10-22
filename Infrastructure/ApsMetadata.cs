using Microsoft.Data.SqlClient;
using System.Data;
using System.Security.Cryptography;

namespace AgenticPitstop.Infrastructure;

/// <summary>
/// Reads the configuration that shapes the portal, and the navigation registry.
///
/// This is the Open-Closed seam. Adding a screen means adding one entry to
/// <see cref="Screens"/>; adding an inbox column, a collection, a detail tab or an
/// agent means writing a row through a procedure. None of it edits a controller,
/// a view or the layout.
/// </summary>
public sealed class ApsMetadata
{
    private readonly ApsDb _db;

    public ApsMetadata(ApsDb db) => _db = db;

    // -------------------------------------------------------------------------
    // Navigation registry
    // -------------------------------------------------------------------------

    /// <summary>
    /// One entry per screen. Roles here drive sidebar visibility only; the real
    /// check is the [ApsAuthorize] attribute on the controller action.
    /// </summary>
    public sealed record ScreenEntry(
        string Key, string Label, string Icon, string Group,
        string Controller, string Action, byte[] Roles);

    public static readonly ScreenEntry[] Screens =
    {
        new("inbox",           "Inbox",            "fa-inbox",           "Work",
            "Inbox", "Index",       new[] { ApsIdentity.RoleAdmin, ApsIdentity.RoleManager, ApsIdentity.RoleAssociate }),
        new("inflow",          "Inflow",           "fa-cloud-arrow-down","Work",
            "Inflow", "Index",      new[] { ApsIdentity.RoleAdmin, ApsIdentity.RoleManager, ApsIdentity.RoleAssociate }),
        new("agent-monitor",   "Agents Monitor",   "fa-gauge-high",      "Operations",
            "Agents", "Monitor",    new[] { ApsIdentity.RoleAdmin }),
        new("agent-scheduler", "Agents Scheduler", "fa-clock",           "Operations",
            "Agents", "Scheduler",  new[] { ApsIdentity.RoleAdmin }),
        new("reports",         "Dynamic Reports",  "fa-chart-column",    "Operations",
           "Reports", "Index",     new[] { ApsIdentity.RoleAdmin}),
        new("user-management", "User Management",  "fa-users-gear",      "Administration",
            "Users", "Index",       new[] { ApsIdentity.RoleAdmin, ApsIdentity.RoleManager }),
        new("configuration",   "Configuration",    "fa-sliders",         "Administration",
            "Configuration", "Index", new[] { ApsIdentity.RoleAdmin }),
        new("client-master",   "Client Master",    "fa-building",        "Administration",
            "Masters", "ClientMaster",  new[] { ApsIdentity.RoleAdmin }),
        new("usecase-master",  "Use Case Master",  "fa-diagram-project", "Administration",
            "Masters", "UseCaseMaster", new[] { ApsIdentity.RoleAdmin }),
        new("usecase-mapping", "Use Case Mapping", "fa-user-check",      "Administration",
            "Masters", "UseCaseMapping", new[] { ApsIdentity.RoleAdmin, ApsIdentity.RoleManager }),
        new("ApiConfig",   "API Configurator",    "fa-server",         "ApiConfig",
            "ApiConfig", "Index", new[] { ApsIdentity.RoleAdmin }),
        new("pipelines", "Collection Pipelines", "fa-diagram-project", "ApiConfig",
            "Pipeline", "Index", new[] { ApsIdentity.RoleAdmin }),
        new("file-roots", "Folder Roots", "fa-folder-tree", "Administration",
            "FileRoots", "Index", new[] { ApsIdentity.RoleAdmin }),
        new("sop-analyzer",    "SOP Analyzer",     "fa-compass",         "Design Tools",
            "SopAnalyzer", "Index", new[] { ApsIdentity.RoleAdmin, ApsIdentity.RoleManager }),
    };

    // -------------------------------------------------------------------------
    // Detail layout vocabularies. The configuration screen offers only these,
    // and the controller rejects anything else.
    // -------------------------------------------------------------------------

    public static readonly string[] DetailIcons =
    {
        "fa-circle-info", "fa-user", "fa-user-tie", "fa-id-card", "fa-file-contract", "fa-file-lines",
        "fa-folder-open", "fa-calendar-days", "fa-clock", "fa-sack-dollar", "fa-money-bill-wave", "fa-coins",
        "fa-building", "fa-hospital", "fa-heart-pulse", "fa-shield-halved", "fa-scale-balanced", "fa-hashtag",
        "fa-location-dot", "fa-phone", "fa-envelope", "fa-flag", "fa-circle-check", "fa-triangle-exclamation",
        "fa-list-check", "fa-table", "fa-table-list", "fa-chart-column", "fa-layer-group", "fa-file-image",
        "fa-magnifying-glass", "fa-note-sticky", "fa-gauge-high", "fa-link", "fa-tag"
    };

    public static readonly string[] DetailAccents = { "navy", "teal", "sky", "green", "gold", "slate" };

    public static readonly string[] DetailStatusColors = { "success", "danger", "warning", "info", "neutral" };

    public static readonly string[] DetailValueFormats = { "text", "number", "currency", "date", "datetime" };
    public static IEnumerable<ScreenEntry> ScreensFor(byte accessType) =>
        Screens.Where(s => Array.IndexOf(s.Roles, accessType) >= 0);

    // -------------------------------------------------------------------------
    // Configuration reads
    // -------------------------------------------------------------------------

    /// <summary>
    /// The active inflow template for a use case. There is one active template
    /// per use case, so the id is required — without it there is no single
    /// answer to "which configuration".
    /// </summary>
    public DataRow InflowConfiguration(int useCaseId)
    {
        if (useCaseId <= 0) return null;

        var data = _db.QueryMultiple("APS_InflowConfig_GetActive",
            p => ApsDb.In(p, "@UseCaseID", useCaseId));

        return data.Tables.Count > 0 && data.Tables[0].Rows.Count > 0
            ? data.Tables[0].Rows[0]
            : null;
    }

    /// <summary>The columns of that template — result set 2 of the same call.</summary>
    public DataTable InflowColumns(int useCaseId)
    {
        if (useCaseId <= 0) return new DataTable();

        var data = _db.QueryMultiple("APS_InflowConfig_GetActive",
            p => ApsDb.In(p, "@UseCaseID", useCaseId));

        return data.Tables.Count > 1 ? data.Tables[1] : new DataTable();
    }

    public DataTable InflowColumns(short cid) => _db.Query("APS_InflowColumnConfig_GetByConfig",p => ApsDb.In(p, "@cid", cid));

    public DataTable ConfigHeaders(short cid) =>
        _db.Query("APS_ConfigHeader_Get", p => ApsDb.In(p, "@cid", cid));

    //public DataTable ConfigurationHistory(int top = 10) =>
    //    _db.Query("APS_Configuration_History_Get", p => ApsDb.In(p, "@top", top));

    ///// <summary>Inbox columns in the Admin-configured order.</summary>
    //public List<string> InboxColumns()
    //{
    //    var table = _db.Query("APS_InboxConfig_Get");
    //    return table.AsEnumerable()
    //                .Select(r => Convert.ToString(r["columnname"]))
    //                .Where(c => !string.IsNullOrWhiteSpace(c))
    //                .ToList();
    //}

    public DataTable ConfigurationHistory(int top = 10) =>
    _db.Query("APS_ConfigHistory_Get", p => ApsDb.In(p, "@Top", top));

    public List<string> InboxColumns(int useCaseId)
    {
        if (useCaseId <= 0) return new List<string>();
        var set = _db.QueryMultiple("APS_InboxConfig_Get", p => ApsDb.In(p, "@UseCaseID", useCaseId));
        if (set.Tables.Count < 2) return new List<string>();
        return set.Tables[1].AsEnumerable()
            .Where(r => Convert.ToInt32(r["GridType"]) == 1)
            .Select(r => Convert.ToString(r["FieldCode"]))
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .ToList();
    }

    public DataRow DetailConfiguration() => _db.QuerySingle("APS_DetailConfig_Get");

    public DataTable DetailTabs(short dcid) =>
        _db.Query("APS_DetailTab_Get", p => ApsDb.In(p, "@dcid", dcid));

    public DataTable DetailFields(short dcid) =>
        _db.Query("APS_DetailField_Get", p => ApsDb.In(p, "@dcid", dcid));

    public void RecordHistory(string item, string detail, string actionBy) =>
        _db.Execute("APS_Configuration_History_Insert", p =>
        {
            ApsDb.In(p, "@configitem", item);
            ApsDb.In(p, "@detail", detail);
            ApsDb.In(p, "@actionby", actionBy);
        });

    // -------------------------------------------------------------------------
    // Shape helpers used by the views
    // -------------------------------------------------------------------------

    /// <summary>
    /// Turns a DataTable into plain dictionaries so a view or a JSON response can
    /// walk it without a model class. DBNull becomes null.
    /// </summary>
    public static List<Dictionary<string, object>> ToList(DataTable table)
    {
        var rows = new List<Dictionary<string, object>>(table?.Rows.Count ?? 0);
        if (table == null) return rows;

        foreach (DataRow row in table.Rows)
        {
            var item = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (DataColumn column in table.Columns)
            {
                var value = row[column];
                item[column.ColumnName] = value == DBNull.Value ? null : value;
            }
            rows.Add(item);
        }
        return rows;
    }

    public static Dictionary<string, object> ToDictionary(DataRow row)
    {
        var item = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        if (row == null) return item;

        foreach (DataColumn column in row.Table.Columns)
        {
            var value = row[column];
            item[column.ColumnName] = value == DBNull.Value ? null : value;
        }
        return item;
    }

    /// <summary>Safe cell read for views: missing column returns empty string.</summary>
    public static string Cell(DataRow row, string columnName)
    {
        if (row == null || !row.Table.Columns.Contains(columnName)) return string.Empty;
        var value = row[columnName];
        return value == DBNull.Value ? string.Empty : Convert.ToString(value);
    }

    public static bool HasColumn(DataTable table, string columnName) =>
        table != null && table.Columns.Contains(columnName);
}

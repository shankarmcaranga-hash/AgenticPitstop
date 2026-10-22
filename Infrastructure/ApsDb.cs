using System.Data;
using Microsoft.Data.SqlClient;

namespace AgenticPitstop.Infrastructure;

/// <summary>
/// The single door to the database.
///
/// Every method here sets CommandType.StoredProcedure. There is no overload that
/// accepts SQL text, which is what keeps the "stored procedures only" rule true
/// by construction rather than by convention: a controller cannot send ad-hoc SQL
/// even if someone tries.
///
/// Results come back as DataTable / DataRow rather than typed classes, because the
/// portal has no model layer and the shape of APS_InflowData is not known until an
/// Admin uploads a template.
/// </summary>
public sealed class ApsDb
{
    private readonly string _connectionString;

    public ApsDb(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("ApsDatabase")
            ?? throw new InvalidOperationException(
                "Connection string 'ApsDatabase' is missing. Add it to appsettings.json or user secrets.");
    }

    private SqlConnection OpenConnection()
    {
        var connection = new SqlConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private static SqlCommand BuildCommand(SqlConnection connection, string procedureName,
                                           Action<SqlParameterCollection> parameters)
    {
        var command = new SqlCommand(procedureName, connection)
        {
            CommandType = CommandType.StoredProcedure,
            CommandTimeout = 120
        };
        parameters?.Invoke(command.Parameters);
        return command;
    }

    /// <summary>Runs a procedure and returns its single result set.</summary>
    public DataTable Query(string procedureName, Action<SqlParameterCollection> parameters = null)
    {
        using var connection = OpenConnection();
        using var command = BuildCommand(connection, procedureName, parameters);
        using var adapter = new SqlDataAdapter(command);

        var table = new DataTable();
        adapter.Fill(table);
        return table;
    }

    /// <summary>Runs a procedure that returns several result sets.</summary>
    public DataSet QueryMultiple(string procedureName, Action<SqlParameterCollection> parameters = null)
    {
        using var connection = OpenConnection();
        using var command = BuildCommand(connection, procedureName, parameters);
        using var adapter = new SqlDataAdapter(command);

        var dataSet = new DataSet();
        adapter.Fill(dataSet);
        return dataSet;
    }

    /// <summary>Returns the first row, or null when the procedure returned nothing.</summary>
    public DataRow QuerySingle(string procedureName, Action<SqlParameterCollection> parameters = null)
    {
        var table = Query(procedureName, parameters);
        return table.Rows.Count > 0 ? table.Rows[0] : null;
    }

    /// <summary>Runs a procedure for its side effects and returns rows affected.</summary>
    public int Execute(string procedureName, Action<SqlParameterCollection> parameters = null)
    {
        using var connection = OpenConnection();
        using var command = BuildCommand(connection, procedureName, parameters);
        return command.ExecuteNonQuery();
    }

    /// <summary>
    /// Runs a procedure and hands the caller its OUTPUT parameters. Used by every
    /// save path, since the APS_ procedures report validation failures through an
    /// @message OUTPUT rather than by throwing.
    /// </summary>
    public IDictionary<string, object> ExecuteWithOutput(
        string procedureName,
        Action<SqlParameterCollection> parameters = null)
    {
        using var connection = OpenConnection();
        using var command = BuildCommand(connection, procedureName, parameters);
        command.ExecuteNonQuery();

        var outputs = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (SqlParameter parameter in command.Parameters)
        {
            if (parameter.Direction is ParameterDirection.Output or ParameterDirection.InputOutput)
            {
                outputs[parameter.ParameterName.TrimStart('@')] =
                    parameter.Value == DBNull.Value ? null : parameter.Value;
            }
        }
        return outputs;
    }

    // -------------------------------------------------------------------------
    // Parameter helpers
    // -------------------------------------------------------------------------

    /// <summary>Adds an input parameter, translating null to DBNull.</summary>
    public static SqlParameter In(SqlParameterCollection parameters, string name, object value)
    {
        return parameters.AddWithValue(name, value ?? DBNull.Value);
    }

    /// <summary>Adds an OUTPUT parameter.</summary>
    public static SqlParameter Out(SqlParameterCollection parameters, string name,
                                   SqlDbType type, int size = 0)
    {
        var parameter = size > 0
            ? parameters.Add(name, type, size)
            : parameters.Add(name, type);
        parameter.Direction = ParameterDirection.Output;
        return parameter;
    }

    /// <summary>
    /// Adds a table-valued parameter. TVPs are how lists reach the procedures —
    /// column selections, field definitions, tab layouts — so nothing has to be
    /// concatenated into a delimited string and re-parsed in T-SQL.
    /// </summary>
    public static SqlParameter Table(SqlParameterCollection parameters, string name,
                                     string typeName, DataTable value)
    {
        var parameter = parameters.AddWithValue(name, value);
        parameter.SqlDbType = SqlDbType.Structured;
        parameter.TypeName = typeName;
        return parameter;
    }

    /// <summary>Empty DataTable shaped like dbo.APS_ColumnListType.</summary>
    public static DataTable NewColumnList()
    {
        var table = new DataTable();
        table.Columns.Add("columnname", typeof(string));
        table.Columns.Add("datatype", typeof(string));
        table.Columns.Add("inputtype", typeof(string));
        table.Columns.Add("sortorder", typeof(short));
        return table;
    }

    /// <summary>Empty DataTable shaped like dbo.APS_HeaderListType.</summary>
    public static DataTable NewHeaderList()
    {
        var table = new DataTable();
        table.Columns.Add("headerkey", typeof(string));
        table.Columns.Add("headervalue", typeof(string));
        table.Columns.Add("sortorder", typeof(short));
        return table;
    }

    /// <summary>Empty DataTable shaped like dbo.APS_DetailTabType.</summary>
    public static DataTable NewDetailTabList()
    {
        var table = new DataTable();
        table.Columns.Add("rowkey", typeof(short));
        table.Columns.Add("parentrowkey", typeof(short));
        table.Columns.Add("mode", typeof(byte));
        table.Columns.Add("tabkey", typeof(string));
        table.Columns.Add("label", typeof(string));
        table.Columns.Add("icon", typeof(string));
        table.Columns.Add("displaytype", typeof(string));
        table.Columns.Add("badge", typeof(short));
        table.Columns.Add("sortorder", typeof(short));
        return table;
    }

    /// <summary>Empty DataTable shaped like dbo.APS_DetailFieldType.</summary>
    public static DataTable NewDetailFieldList()
    {
        var table = new DataTable();
        table.Columns.Add("rowkey", typeof(short));
        table.Columns.Add("mode", typeof(byte));
        table.Columns.Add("fieldname", typeof(string));
        table.Columns.Add("isrequired", typeof(bool));
        table.Columns.Add("sortorder", typeof(short));
        return table;
    }
    /// <summary>Empty DataTable shaped like dbo.APS_InflowColumnType (column order matters).</summary>

    /// <summary>Empty DataTable shaped like dbo.APS_CollectionColumnType (column order matters).</summary>
    public static DataTable NewCollectionColumnList()
    {
        var table = new DataTable();
        table.Columns.Add("ColumnName", typeof(string));
        table.Columns.Add("DataTypeName", typeof(string));
        table.Columns.Add("ColumnLength", typeof(int));
        table.Columns.Add("NumericPrecision", typeof(byte));
        table.Columns.Add("NumericScale", typeof(byte));
        table.Columns.Add("IsNullable", typeof(byte));
        table.Columns.Add("ConstraintType", typeof(string));
        table.Columns.Add("DefaultValue", typeof(string));
        table.Columns.Add("CheckExpression", typeof(string));
        table.Columns.Add("RefTableName", typeof(string));
        table.Columns.Add("RefColumnName", typeof(string));
        table.Columns.Add("IsIdentityColumn", typeof(byte));
        table.Columns.Add("IsSystemColumn", typeof(byte));
        table.Columns.Add("SortOrder", typeof(int));
        return table;
    }

    /// <summary>Empty DataTable shaped like dbo.APS_DetailLayoutTileType.</summary>
    public static DataTable NewDetailLayoutTileList()
    {
        var table = new DataTable();
        table.Columns.Add("SortOrder", typeof(int));
        table.Columns.Add("Title", typeof(string));
        table.Columns.Add("Icon", typeof(string));
        table.Columns.Add("Accent", typeof(string));
        table.Columns.Add("SourceType", typeof(string));
        table.Columns.Add("SourceName", typeof(string));
        table.Columns.Add("ValueColumn", typeof(string));
        table.Columns.Add("SubtitleColumn", typeof(string));
        table.Columns.Add("ValueFormat", typeof(string));
        table.Columns.Add("SourceSpecJson", typeof(string));
        table.Columns.Add("SubtitleSpecJson", typeof(string));
        return table;
    }

    /// <summary>Empty DataTable shaped like dbo.APS_DetailLayoutTabType.</summary>
    public static DataTable NewDetailLayoutTabList()
    {
        var table = new DataTable();
        table.Columns.Add("SortOrder", typeof(int));
        table.Columns.Add("Label", typeof(string));
        table.Columns.Add("Icon", typeof(string));
        table.Columns.Add("DisplayType", typeof(string));
        table.Columns.Add("SettingsJson", typeof(string));
        return table;
    }
    public static DataTable NewInflowColumnList()
    {
        var table = new DataTable();
        table.Columns.Add("ExcelColumnName", typeof(string));
        table.Columns.Add("FieldCode", typeof(string));
        table.Columns.Add("DataType", typeof(string));
        table.Columns.Add("ColumnLength", typeof(int));
        table.Columns.Add("IsRequired", typeof(byte));
        table.Columns.Add("ShowInInbox", typeof(byte));
        table.Columns.Add("ShowInDetail", typeof(byte));
        table.Columns.Add("SortOrder", typeof(int));
        return table;
    }
}

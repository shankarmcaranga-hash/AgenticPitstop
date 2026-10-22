using Microsoft.Data.SqlClient;
using System.Data;
using System.Globalization;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AgenticPitstop.Infrastructure;
/// <summary>
/// Where a detail screen reads its data. The same shape serves a tile, a table
/// tab, a checklist and a notes card, so one reader covers them all.
/// </summary>
public sealed class ApsDataSourceSpec
{
    public string Kind { get; set; } = "TABLE";   // TABLE | JOIN | PROC | INFLOW
    public string Procedure { get; set; }         // PROC
    public string JsonColumn { get; set; }        // a column holding JSON to read inside
    public string JsonRoot { get; set; }          // optional path within that JSON

    public bool IsJson => !string.IsNullOrWhiteSpace(JsonColumn);
}

/// <summary>
/// Reads a use case's detail layout and turns it into what the Detail screen
/// shows for one record. Everything here is scoped to a single InflowDataID:
/// tables are filtered by it, procedures receive it, and document rows that
/// belong to another record are dropped.
/// </summary>
public sealed class ApsDetailRuntime
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private readonly ApsDb _db;
    private readonly IConfiguration _configuration;
    private readonly ApsFileRoots _fileRoots;
    private readonly Dictionary<string, bool> _approvedProcedures = new(StringComparer.OrdinalIgnoreCase);

    public ApsDetailRuntime(ApsDb db, IConfiguration configuration, ApsFileRoots fileRoots)
    {
        _db = db;
        _configuration = configuration;
        _fileRoots = fileRoots;
    }

    // -------------------------------------------------------------------------
    // Layout
    // -------------------------------------------------------------------------

    public sealed record Tile(int Id, byte Mode, string Title, string Icon, string Accent, string SourceType,
                          string SourceName, string ValueColumn, string SubtitleColumn, string ValueFormat,
                          string SourceSpecJson, string SubtitleSpecJson);

    public sealed record Tab(int Id, byte Mode, string Label, string Icon, string DisplayType, string SettingsJson);

    public sealed record TileView(string Title, string Icon, string Accent, string Value, string Subtitle, string Error);

    public sealed record ResolvedDocument(string FullPath, string Name, string Extension, string Error);

    public sealed class Layout
    {
        public List<byte> Modes { get; } = new();
        public List<Tile> Tiles { get; } = new();
        public List<Tab> Tabs { get; } = new();

        /// <summary>One source feeding several tiles, when the layout uses that style.</summary>
        public string TileSourceSpecJson { get; set; }
    }

    public Layout LoadLayout(int useCaseId)
    {
        var set = _db.QueryMultiple("APS_DetailLayout_Get", p => ApsDb.In(p, "@UseCaseID", useCaseId));
        var layout = new Layout();

        if (set.Tables.Count > 0)
            foreach (DataRow r in set.Tables[0].Rows)
            {
                layout.Modes.Add(Convert.ToByte(r["WorkspaceMode"]));
                var shared = Cell(r, "TileSourceSpecJson");
                if (!string.IsNullOrWhiteSpace(shared)) layout.TileSourceSpecJson = shared;
            }

        if (set.Tables.Count > 1)
            foreach (DataRow r in set.Tables[1].Rows)
                layout.Tiles.Add(new Tile(
                    Convert.ToInt32(r["DetailLayoutTileID"]), Convert.ToByte(r["WorkspaceMode"]),
                    Cell(r, "Title"), Cell(r, "Icon"), Cell(r, "Accent"), Cell(r, "SourceType"),
                    Cell(r, "SourceName"), Cell(r, "ValueColumn"), Cell(r, "SubtitleColumn"), Cell(r, "ValueFormat"),
                    Cell(r, "SourceSpecJson"), Cell(r, "SubtitleSpecJson")));

        if (set.Tables.Count > 2)
            foreach (DataRow r in set.Tables[2].Rows)
                layout.Tabs.Add(new Tab(
                    Convert.ToInt32(r["DetailLayoutTabID"]), Convert.ToByte(r["WorkspaceMode"]),
                    Cell(r, "Label"), Cell(r, "Icon"), Cell(r, "DisplayType"), Cell(r, "SettingsJson")));

        return layout;
    }

    // -------------------------------------------------------------------------
    // Tiles
    // -------------------------------------------------------------------------

    public List<TileView> BuildTiles(IEnumerable<Tile> tiles, int inflowDataId, IReadOnlyDictionary<string, string> rowData,
                                 int useCaseId = 0, string sharedSpecJson = null)
    {
        var list = tiles.ToList();

        // Each distinct source is read once, however many tiles use it.
        var cache = new Dictionary<string, (Dictionary<string, object> Row, string Error)>(StringComparer.Ordinal);

        (Dictionary<string, object> Row, string Error) Read(string key, Func<Dictionary<string, object>> read)
        {
            if (cache.TryGetValue(key, out var hit)) return hit;

            try { cache[key] = (read(), null); }
            catch (Exception ex) when (ex is SqlException or InvalidOperationException or JsonException)
            {
                cache[key] = (null, "This value could not be read.");
            }
            return cache[key];
        }

        // A shared source means one procedure or join fills every tile.
        var shared = string.IsNullOrWhiteSpace(sharedSpecJson)
            ? (null, null)
            : Read("shared", () => ReadSpec(sharedSpecJson, inflowDataId, useCaseId, 1).FirstOrDefault());

        return list.Select(t =>
        {
            (Dictionary<string, object> Row, string Error) value, subtitle;

            if (!string.IsNullOrWhiteSpace(t.SourceSpecJson))
            {
                value = Read("v:" + t.SourceSpecJson,
                             () => ReadSpec(t.SourceSpecJson, inflowDataId, useCaseId, 1).FirstOrDefault());

                subtitle = string.IsNullOrWhiteSpace(t.SubtitleSpecJson)
                    ? value
                    : Read("s:" + t.SubtitleSpecJson,
                           () => ReadSpec(t.SubtitleSpecJson, inflowDataId, useCaseId, 1).FirstOrDefault());
            }
            else if (shared.Item1 != null || shared.Item2 != null)
            {
                value = subtitle = shared;
            }
            else
            {
                // The original style: a table, a procedure or the inflow record.
                // The key must include the columns. ReadRows returns only what it
                // was asked for, so two tiles on the same source are not the same
                // read — without this the second tile gets the first tile's row.
                value = subtitle = Read("o:" + t.SourceType + "|" + t.SourceName +
                                        "|" + t.ValueColumn + "|" + t.SubtitleColumn, () =>
                    ReadRows(t.SourceType, t.SourceName, new[] { t.ValueColumn, t.SubtitleColumn },
                             inflowDataId, rowData, 1).FirstOrDefault());
            }

            object Get((Dictionary<string, object> Row, string Error) from, string column) =>
                from.Row != null && !string.IsNullOrEmpty(column) && from.Row.TryGetValue(column, out var v) ? v : null;

            return new TileView(t.Title, t.Icon, t.Accent,
                                Format(Get(value, t.ValueColumn), t.ValueFormat),
                                string.IsNullOrEmpty(t.SubtitleColumn) ? string.Empty : CellText(Get(subtitle, t.SubtitleColumn)),
                                value.Error ?? subtitle.Error);
        }).ToList();
    }

    private static string SourceKey(string type, string name) => type + "|" + (name ?? string.Empty);

    // -------------------------------------------------------------------------
    // Tabs
    // -------------------------------------------------------------------------

    public object BuildTab(Tab tab, int inflowDataId, IReadOnlyDictionary<string, string> rowData, int useCaseId = 0)
    {
        using var settings = JsonDocument.Parse(tab.SettingsJson);
        var s = settings.RootElement.Clone();

        return tab.DisplayType switch
        {
            "TABLE" => BuildTable(s, inflowDataId, rowData, useCaseId),
            "SUMMARY" => BuildSummary(s, inflowDataId, rowData, useCaseId),
            "CHECKLIST" => BuildChecklist(s, inflowDataId, useCaseId),
            "DOCVIEWER" => BuildDocuments(s, inflowDataId),
            "NOTES" => BuildNotes(s, inflowDataId, useCaseId),
            _ => throw new InvalidOperationException("This tab has an unknown display type.")
        };
    }

    /// <summary>A tab reads either a saved spec or its original source settings.</summary>
    private List<Dictionary<string, object>> ReadTabRows(JsonElement s, IEnumerable<string> columns,
                                                          int inflowDataId, int useCaseId,
                                                          IReadOnlyDictionary<string, string> rowData, int top)
    {
        var spec = S(s, "specJson");
        return string.IsNullOrWhiteSpace(spec)
            ? ReadRows(S(s, "sourceType"), S(s, "sourceName"), columns, inflowDataId, rowData, top)
            : ReadSpec(spec, inflowDataId, useCaseId, top);
    }

    private object BuildTable(JsonElement s, int inflowDataId, IReadOnlyDictionary<string, string> rowData, int useCaseId)
    {
        var columns = Arr(s, "columns")
            .Select(c => (Column: S(c, "column"), Header: S(c, "header")))
            .Where(c => !string.IsNullOrEmpty(c.Column))
            .ToList();

        var rows = ReadTabRows(s, columns.Select(c => c.Column), inflowDataId, useCaseId, rowData, 1000);

        return new
        {
            type = "TABLE",
            pageSize = I(s, "pageSize") ?? 25,
            columns = columns.Select(c => new { header = string.IsNullOrEmpty(c.Header) ? c.Column : c.Header }),
            rows = rows.Select(r => columns
                .Select(c => CellText(r.TryGetValue(c.Column, out var v) ? v : null))
                .ToList()).ToList()
        };
    }

    private object BuildSummary(JsonElement s, int inflowDataId, IReadOnlyDictionary<string, string> rowData, int useCaseId)
    {
        var cards = Arr(s, "cards").Select(c => new
        {
            title = S(c, "title"),
            icon = S(c, "icon"),
            accent = S(c, "accent"),
            column = S(c, "column"),
            format = S(c, "format") ?? "text"
        }).ToList();

        var row = ReadTabRows(s, cards.Select(c => c.column), inflowDataId, useCaseId, rowData, 1).FirstOrDefault();

        return new
        {
            type = "SUMMARY",
            cards = cards.Select(c => new
            {
                c.title,
                c.icon,
                c.accent,
                value = row != null && row.TryGetValue(c.column ?? string.Empty, out var v) ? Format(v, c.format) : string.Empty
            })
        };
    }

    private object BuildChecklist(JsonElement s, int inflowDataId, int useCaseId)
    {
        // Questions come from the master, filtered to this use case.
        var questions = _db.Query("APS_ChecklistQuestion_GetForUseCase", p => ApsDb.In(p, "@UseCaseID", useCaseId))
            .AsEnumerable()
            .Select(r => new
            {
                Id = int.TryParse(ApsMetadata.Cell(r, "QuestionId"), out var id) ? id : 0,
                Question = ApsMetadata.Cell(r, "Question"),
                Category = ApsMetadata.Cell(r, "Category"),
                Sort = int.TryParse(ApsMetadata.Cell(r, "SortOrder"), out var so) ? so : 0
            })
            .Where(q => q.Id > 0)
            .ToList();

        // A saved list narrows it further; with none, every mapped question shows.
        var chosen = Arr(s, "questionIds")
            .Where(q => q.ValueKind == JsonValueKind.Number && q.TryGetInt32(out _))
            .Select(q => q.GetInt32())
            .ToHashSet();

        if (chosen.Count > 0) questions = questions.Where(q => chosen.Contains(q.Id)).ToList();

        var colors = Arr(s, "colors")
            .Select(c => (Value: S(c, "value"), Color: S(c, "color")))
            .Where(c => !string.IsNullOrEmpty(c.Value))
            .ToList();

        // Evidence and results come from a collection, keyed by question.
        var evidence = ReadEvidence(s, inflowDataId, useCaseId, out var evidenceColumns,
                                    out var resultColumn, out var justifyColumn, out var evaluated);

        var items = questions.OrderBy(q => q.Sort).Select(q =>
        {
            evidence.TryGetValue(q.Id, out var row);

            string Value(string column) =>
                row != null && !string.IsNullOrEmpty(column) && row.TryGetValue(column, out var v) ? CellText(v) : string.Empty;

            var result = !evaluated ? "Not evaluated"
                       : row == null ? "No result"
                       : string.IsNullOrWhiteSpace(Value(resultColumn)) ? "Pending" : Value(resultColumn);

            var color = colors.FirstOrDefault(c => string.Equals(c.Value, result, StringComparison.OrdinalIgnoreCase)).Color;

            return new
            {
                id = q.Id,
                question = q.Question,
                category = q.Category,
                result,
                justification = Value(justifyColumn),
                color = ApsMetadata.DetailStatusColors.Contains(color) ? color : "neutral",
                evidence = evidenceColumns.Select(c => new { label = c.Header, value = Value(c.Column) })
                                          .Where(e => !string.IsNullOrWhiteSpace(e.value))
                                          .ToList()
            };
        }).ToList();

        return new
        {
            type = "CHECKLIST",
            evaluated,
            items,
            summary = items.GroupBy(i => i.result)
                           .Select(g => new { result = g.Key, count = g.Count(), color = g.First().color })
        };
    }
    /// <summary>
    /// Notes as cards: a live feed and an AI summary of the same record, each
    /// read from its own source. A card is Source, Date and Content.
    /// </summary>
    private object BuildNotes(JsonElement s, int inflowDataId, int useCaseId)
    {
        return new
        {
            type = "NOTES",
            live = BuildNoteFeed(Obj(s, "live"), inflowDataId, useCaseId),
            summary = BuildNoteFeed(Obj(s, "summary"), inflowDataId, useCaseId),
            refreshPipelineId = I(s, "refreshPipelineId") ?? 0,
            defaultView = S(s, "defaultView") ?? "live"
        };
    }

    private object BuildNoteFeed(JsonElement feed, int inflowDataId, int useCaseId)
    {
        var spec = S(feed, "specJson");
        if (string.IsNullOrWhiteSpace(spec)) return new { configured = false, cards = new List<object>() };

        var sourceColumn = S(feed, "sourceColumn");
        var dateColumn = S(feed, "dateColumn");
        var contentColumn = S(feed, "contentColumn");
        var descending = (S(feed, "order") ?? "desc") != "asc";

        List<Dictionary<string, object>> rows;
        try
        {
            rows = ReadSpec(spec, inflowDataId, useCaseId, 500);
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException or JsonException)
        {
            return new { configured = true, error = "These notes could not be read.", cards = new List<object>() };
        }

        var cards = rows.Select(r =>
        {
            string Value(string column) =>
                !string.IsNullOrEmpty(column) && r.TryGetValue(column, out var v) ? CellText(v) : string.Empty;

            var raw = Value(dateColumn);
            var sortable = TryDate(r.TryGetValue(dateColumn ?? string.Empty, out var dv) ? dv : null, raw, out var parsed)
                ? parsed
                : DateTime.MinValue;

            return new
            {
                source = Value(sourceColumn),
                date = sortable == DateTime.MinValue ? raw : sortable.ToString("dd MMM yyyy HH:mm", Inv),
                content = Value(contentColumn),
                sort = sortable
            };
        }).ToList();

        cards = descending
            ? cards.OrderByDescending(c => c.sort).ToList()
            : cards.OrderBy(c => c.sort).ToList();

        return new
        {
            configured = true,
            cards = cards.Select(c => new { c.source, c.date, c.content }).ToList()
        };
    }

    /// <summary>
    /// Evidence rows for one record, keyed by question. The source is built in
    /// the same way as any other, so it may join several tables.
    /// </summary>
    private Dictionary<int, Dictionary<string, object>> ReadEvidence(
        JsonElement s, int inflowDataId, int useCaseId,
        out List<(string Column, string Header)> evidenceColumns,
        out string resultColumn, out string justifyColumn, out bool evaluated)
    {
        evidenceColumns = Arr(s, "evidenceColumns")
            .Select(c => (Column: S(c, "column"), Header: S(c, "header") ?? S(c, "column")))
            .Where(c => !string.IsNullOrEmpty(c.Column))
            .ToList();

        var ev = Obj(s, "evidence");
        resultColumn = S(ev, "resultColumn");
        justifyColumn = S(ev, "justificationColumn");
        var questionColumn = S(ev, "questionColumn") ?? "QuestionId";
        var spec = S(ev, "specJson");

        // The older shape: a procedure mapped column by column.
        var legacy = Obj(s, "resultSource");
        var procedure = S(legacy, "procedure");

        var map = new Dictionary<int, Dictionary<string, object>>();
        evaluated = false;

        List<Dictionary<string, object>> rows;

        if (!string.IsNullOrWhiteSpace(spec))
        {
            rows = ReadSpec(spec, inflowDataId, useCaseId, 1000);
        }
        else if (!string.IsNullOrWhiteSpace(procedure))
        {
            rows = ApsMetadata.ToList(RunProcedure(procedure, inflowDataId));
            questionColumn = S(legacy, "questionColumn") ?? questionColumn;
            resultColumn ??= S(legacy, "resultColumn");
            justifyColumn ??= S(legacy, "commentColumn");
        }
        else
        {
            return map;
        }

        evaluated = true;

        foreach (var row in rows)
        {
            if (!row.TryGetValue(questionColumn, out var qv) || !int.TryParse(CellText(qv), out var questionId))
                continue;

            map[questionId] = row;
        }

        return map;
    }

    private object BuildDocuments(JsonElement s, int inflowDataId)
    {
        var documents = CollectDocuments(s, inflowDataId);

        var summaryField = S(s, "summaryField");
        if (string.IsNullOrWhiteSpace(summaryField)) summaryField = "Summary";

        bool IsSummary(DocField f) => string.Equals(f.Field, summaryField, StringComparison.OrdinalIgnoreCase);

        return new
        {
            type = "DOCVIEWER",
            sections = Arr(s, "sections").Select(x => new { key = S(x, "key"), width = I(x, "width") ?? 4 }).ToList(),
            documents = documents.Select(d => new
            {
                token = d.Token,
                name = d.Name,
                ext = d.Extension,
                kind = PreviewKind(d.Extension),
                fieldCount = d.Fields.Count(f => !IsSummary(f)),
                pageCount = d.Fields.Count == 0 ? 1 : d.Fields.Max(f => f.Page),
                pages = d.Fields.GroupBy(f => f.Page).OrderBy(g => g.Key).Select(g => new
                {
                    page = g.Key,
                    summary = g.Where(IsSummary).OrderBy(f => f.Order).Select(f => f.Value).FirstOrDefault(),
                    fields = g.Where(f => !IsSummary(f)).OrderBy(f => f.Order)
                              .Select(f => new { field = f.Field, value = f.Value, rowKey = f.RowKey }).ToList()
                }).ToList()
            }).ToList(),
            canEdit = Obj(s, "edit").ValueKind == JsonValueKind.Object && B(Obj(s, "edit"), "enabled")
        };
    }

    private static bool B(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) &&
        (v.ValueKind == JsonValueKind.True || (v.ValueKind == JsonValueKind.String && bool.TryParse(v.GetString(), out var b) && b));

    // -------------------------------------------------------------------------
    // Documents
    // -------------------------------------------------------------------------

    private sealed record DocField(string Field, string Value, int Page, int Order, string RowKey);

    private sealed class DocEntry
    {
        public string Token { get; init; }
        public string FilePath { get; init; }
        public string Name { get; init; }
        public string Extension { get; init; }
        public List<DocField> Fields { get; } = new();
    }

    private List<DocEntry> CollectDocuments(JsonElement s, int inflowDataId)
    {
        var map = Obj(s, "map");
        var pathColumn = S(map, "docPath") ?? string.Empty;
        var nameColumn = S(map, "docName") ?? string.Empty;
        var idColumn = S(map, "inflowDataId") ?? string.Empty;
        var fieldColumn = S(map, "field") ?? string.Empty;
        var valueColumn = S(map, "value") ?? string.Empty;
        var pageColumn = S(map, "pageNo") ?? string.Empty;
        var orderColumn = S(map, "fieldOrder") ?? string.Empty;
        var keyColumn = S(map, "fieldKey") ?? string.Empty;
        var recordId = inflowDataId.ToString(Inv);

        var list = new List<DocEntry>();
        var byToken = new Dictionary<string, DocEntry>();

        foreach (var row in ApsMetadata.ToList(RunProcedure(S(s, "procedure"), inflowDataId)))
        {
            // Only rows that belong to this record, whatever the procedure returned.
            // Compare as numbers so "123", " 123" and 123.0 all match this record.
            if (!row.TryGetValue(idColumn, out var rowId) ||
                !decimal.TryParse(CellText(rowId).Trim(), NumberStyles.Any, Inv, out var rowIdNumber) ||
                rowIdNumber != inflowDataId)
                continue;

            var path = CellText(row.TryGetValue(pathColumn, out var p) ? p : null).Trim();
            if (path.Length == 0) continue;

            var token = DocumentToken(path);
            if (!byToken.TryGetValue(token, out var entry))
            {
                var extension = Path.GetExtension(path).ToLowerInvariant();
                var name = CellText(row.TryGetValue(nameColumn, out var n) ? n : null).Trim();
                if (name.Length == 0) name = Path.GetFileName(path);
                if (!name.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) name += extension;

                entry = new DocEntry { Token = token, FilePath = path, Name = name, Extension = extension };
                byToken[token] = entry;
                list.Add(entry);
            }

            var field = CellText(row.TryGetValue(fieldColumn, out var f) ? f : null);
            if (field.Length == 0) continue;

            // Page and order are optional: without them everything sits on page 1
            // in the order the procedure returned it.
            var page = 1;
            if (pageColumn.Length > 0 && row.TryGetValue(pageColumn, out var pageValue) &&
                int.TryParse(CellText(pageValue), out var parsedPage) && parsedPage > 0)
                page = parsedPage;

            var order = entry.Fields.Count + 1;
            if (orderColumn.Length > 0 && row.TryGetValue(orderColumn, out var orderValue) &&
                int.TryParse(CellText(orderValue), out var parsedOrder))
                order = parsedOrder;

            // The key of the row this value came from, so a reviewer can edit it.
            var rowKey = keyColumn.Length > 0 && row.TryGetValue(keyColumn, out var kv) ? CellText(kv) : null;

            entry.Fields.Add(new DocField(field, CellText(row.TryGetValue(valueColumn, out var v) ? v : null),
                                          page, order, rowKey));
        }

        return list;
    }
    /// <summary>
    /// Changes one extracted value. The table and columns come from the saved tab
    /// settings, never from the browser, and SQL scopes the update to this record.
    /// </summary>
    public (bool Ok, string Message) UpdateField(Tab tab, int inflowDataId, string rowKey, string value, string actionBy)
    {
        if (tab.DisplayType != "DOCVIEWER") return (false, "This tab cannot be edited.");
        if (string.IsNullOrWhiteSpace(rowKey)) return (false, "That row cannot be identified.");

        using var settings = JsonDocument.Parse(tab.SettingsJson);
        var edit = Obj(settings.RootElement, "edit");

        if (!B(edit, "enabled")) return (false, "Editing is not enabled for this tab.");

        var table = S(edit, "table");
        var keyColumn = S(edit, "keyColumn");
        var valueColumn = S(edit, "valueColumn");

        if (string.IsNullOrWhiteSpace(table) || string.IsNullOrWhiteSpace(keyColumn) || string.IsNullOrWhiteSpace(valueColumn))
            return (false, "Editing is not fully configured for this tab.");

        var outputs = _db.ExecuteWithOutput("APS_Collection_UpdateCell", p =>
        {
            ApsDb.In(p, "@TableName", table);
            ApsDb.In(p, "@KeyColumn", keyColumn);
            ApsDb.In(p, "@KeyValue", rowKey);
            ApsDb.In(p, "@ValueColumn", valueColumn);
            ApsDb.In(p, "@NewValue", value ?? string.Empty);
            ApsDb.In(p, "@InflowDataID", inflowDataId);
            ApsDb.In(p, "@ActionBy", actionBy);
            ApsDb.Out(p, "@Success", SqlDbType.TinyInt);
            ApsDb.Out(p, "@Message", SqlDbType.VarChar, 400);
        });

        var ok = outputs.TryGetValue("Success", out var s) && s != null && Convert.ToInt32(s) == 1;
        return (ok, Convert.ToString(outputs.TryGetValue("Message", out var m) ? m : null));
    }
    public ResolvedDocument ResolveDocument(Tab tab, int inflowDataId, string token)
    {
        if (tab.DisplayType != "DOCVIEWER" || string.IsNullOrWhiteSpace(token))
            return new ResolvedDocument(null, null, null, "Document not found.");

        using var settings = JsonDocument.Parse(tab.SettingsJson);
        var entry = CollectDocuments(settings.RootElement, inflowDataId)
            .FirstOrDefault(d => string.Equals(d.Token, token, StringComparison.Ordinal));

        if (entry == null)
            return new ResolvedDocument(null, null, null, "Document not found for this record.");

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(entry.FilePath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return new ResolvedDocument(null, null, null, "The document path is not valid.");
        }

        if (!IsUnderDocumentRoot(fullPath))
            return new ResolvedDocument(null, null, null, "This document is outside the configured document folders.");

        if (!File.Exists(fullPath))
            return new ResolvedDocument(null, null, null, "The document file could not be found.");

        return new ResolvedDocument(fullPath, entry.Name, entry.Extension, null);
    }

    private bool IsUnderDocumentRoot(string fullPath)
    {
        // The configured document folders, plus the folders pipelines write to.
        var roots = _fileRoots.ReadableRoots();

        foreach (var root in roots.Where(r => !string.IsNullOrWhiteSpace(r)))
        {
            var normalized = Path.GetFullPath(root)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

            if (fullPath.StartsWith(normalized, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;   // no roots configured means nothing is served
    }

    private static string DocumentToken(string path) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path.Trim().ToLowerInvariant())))[..32];

    public static string PreviewKind(string extension) => extension switch
    {
        ".pdf" => "pdf",
        ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp" => "image",
        ".tif" or ".tiff" => "tiff",
        _ => "none"
    };

    public static string ContentTypeFor(string extension) => extension switch
    {
        ".pdf" => "application/pdf",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".gif" => "image/gif",
        ".bmp" => "image/bmp",
        ".webp" => "image/webp",
        ".tif" or ".tiff" => "image/tiff",
        ".doc" => "application/msword",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        _ => "application/octet-stream"
    };

    [SupportedOSPlatform("windows")]
    public static int CountTiffPages(string path)
    {
        using var image = System.Drawing.Image.FromFile(path);
        return image.GetFrameCount(new System.Drawing.Imaging.FrameDimension(image.FrameDimensionsList[0]));
    }

    [SupportedOSPlatform("windows")]
    public static byte[] RenderTiffPage(string path, int page)
    {
        using var image = System.Drawing.Image.FromFile(path);
        var dimension = new System.Drawing.Imaging.FrameDimension(image.FrameDimensionsList[0]);
        var count = image.GetFrameCount(dimension);

        image.SelectActiveFrame(dimension, Math.Clamp(page, 1, count) - 1);

        using var output = new MemoryStream();
        image.Save(output, System.Drawing.Imaging.ImageFormat.Png);
        return output.ToArray();
    }

    // -------------------------------------------------------------------------
    // Reading sources
    // -------------------------------------------------------------------------

    public List<Dictionary<string, object>> ReadRows(string sourceType, string sourceName, IEnumerable<string> columns,
                                                     int inflowDataId, IReadOnlyDictionary<string, string> rowData, int top)
    {
        var wanted = columns.Where(c => !string.IsNullOrWhiteSpace(c))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList();

        switch (sourceType)
        {
            case "INFLOW":
                // The whole record, not only the asked-for columns. It is already
                // in memory, so one row serves every tile reading the inflow source.
                var row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                foreach (var pair in rowData) row[pair.Key] = pair.Value;
                foreach (var c in wanted) if (!row.ContainsKey(c)) row[c] = null;
                return new List<Dictionary<string, object>> { row };

            case "TABLE":
                if (wanted.Count == 0) return new List<Dictionary<string, object>>();
                var table = _db.Query("APS_Detail_ReadTable", p =>
                {
                    ApsDb.In(p, "@TableName", sourceName);
                    ApsDb.In(p, "@Columns", string.Join(",", wanted));
                    ApsDb.In(p, "@InflowDataID", inflowDataId);
                    ApsDb.In(p, "@Top", top);
                });
                return ApsMetadata.ToList(table);

            case "PROC":
                return ApsMetadata.ToList(RunProcedure(sourceName, inflowDataId)).Take(top).ToList();

            default:
                throw new InvalidOperationException("This item has an unknown source type.");
        }
    }
    private static readonly JsonSerializerOptions SpecJson = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// Reads a saved source spec for one record. The spec is passed through to
    /// SQL, which checks every table and column against the catalogue, so nothing
    /// configured on the screen can widen what a reviewer sees.
    /// </summary>
    public List<Dictionary<string, object>> ReadSpec(string specJson, int inflowDataId, int useCaseId, int top = 500)
    {
        if (string.IsNullOrWhiteSpace(specJson)) return new List<Dictionary<string, object>>();

        var table = _db.Query("APS_DataSource_Read", p =>
        {
            ApsDb.In(p, "@SpecJson", specJson);
            ApsDb.In(p, "@InflowDataID", inflowDataId);
            ApsDb.In(p, "@UseCaseID", useCaseId);
            ApsDb.In(p, "@Top", top);
        });

        var rows = ApsMetadata.ToList(table);

        // A source may point at a column holding JSON; those rows are expanded
        // so a path reads inside the document rather than at the column.
        ApsDataSourceSpec spec;
        try { spec = JsonSerializer.Deserialize<ApsDataSourceSpec>(specJson, SpecJson); }
        catch (JsonException) { return rows; }

        return spec is { IsJson: true } ? ExpandJson(rows, spec) : rows;
    }

    /// <summary>Turns a JSON column into ordinary columns, so mapping works the same way.</summary>
    private static List<Dictionary<string, object>> ExpandJson(List<Dictionary<string, object>> rows, ApsDataSourceSpec spec)
    {
        var expanded = new List<Dictionary<string, object>>();

        foreach (var row in rows)
        {
            if (!row.TryGetValue(spec.JsonColumn, out var raw) || raw == null) { expanded.Add(row); continue; }

            JsonNode node;
            try { node = JsonNode.Parse(CellText(raw)); }
            catch (JsonException) { expanded.Add(row); continue; }

            if (!string.IsNullOrWhiteSpace(spec.JsonRoot))
                node = SelectNode(node, spec.JsonRoot);

            foreach (var item in node is JsonArray array ? array.AsEnumerable() : new[] { node })
            {
                var copy = new Dictionary<string, object>(row, StringComparer.OrdinalIgnoreCase);
                Flatten(item, string.Empty, copy);
                expanded.Add(copy);
            }
        }

        return expanded;
    }

    /// <summary>a.b.c naming, so a nested value is addressed like any column.</summary>
    private static void Flatten(JsonNode node, string prefix, IDictionary<string, object> into)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var pair in obj)
                    Flatten(pair.Value, prefix.Length == 0 ? pair.Key : prefix + "." + pair.Key, into);
                break;
            case JsonArray arr:
                for (var i = 0; i < arr.Count && i < 50; i++)
                    Flatten(arr[i], $"{prefix}[{i}]", into);
                break;
            case JsonValue value:
                into[prefix] = value.TryGetValue<string>(out var s) ? s : value.ToJsonString();
                break;
        }
    }

    private static JsonNode SelectNode(JsonNode root, string path)
    {
        var current = root;
        foreach (var part in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (current is JsonObject obj && obj.TryGetPropertyValue(part, out var next)) current = next;
            else return current;
        }
        return current;
    }
    public DataTable RunProcedure(string procedureName, int inflowDataId)
    {
        if (!IsDetailProcedure(procedureName))
            throw new InvalidOperationException($"'{procedureName}' is not an approved detail procedure.");

        return _db.Query("dbo." + procedureName, p => ApsDb.In(p, "@InflowDataID", inflowDataId));
    }

    private bool IsDetailProcedure(string name)
    {
        if (string.IsNullOrWhiteSpace(name) ||
            !System.Text.RegularExpressions.Regex.IsMatch(name, "^[A-Za-z][A-Za-z0-9_]*$"))
            return false;

        if (_approvedProcedures.TryGetValue(name, out var known)) return known;

        var outputs = _db.ExecuteWithOutput("APS_Schema_IsDetailProcedure", p =>
        {
            ApsDb.In(p, "@ProcedureName", name);
            ApsDb.Out(p, "@IsValid", SqlDbType.TinyInt);
        });

        var ok = outputs.TryGetValue("IsValid", out var value) && value != null && Convert.ToInt32(value) == 1;
        _approvedProcedures[name] = ok;
        return ok;
    }

    // -------------------------------------------------------------------------
    // Formatting
    // -------------------------------------------------------------------------

    public static string CellText(object value) => value switch
    {
        null => string.Empty,
        DBNull => string.Empty,
        DateTime d => d.TimeOfDay == TimeSpan.Zero ? d.ToString("dd MMM yyyy", Inv) : d.ToString("dd MMM yyyy HH:mm", Inv),
        DateTimeOffset o => o.ToString("dd MMM yyyy HH:mm", Inv),
        bool b => b ? "Yes" : "No",
        IFormattable f => f.ToString(null, Inv),
        _ => Convert.ToString(value, Inv) ?? string.Empty
    };

    public static string Format(object value, string format)
    {
        var text = CellText(value);
        if (text.Length == 0) return text;

        switch (format)
        {
            case "number":
                return decimal.TryParse(text, NumberStyles.Any, Inv, out var n) ? n.ToString("#,##0.##", Inv) : text;
            case "currency":
                return decimal.TryParse(text, NumberStyles.Any, Inv, out var c) ? c.ToString("#,##0.00", Inv) : text;
            case "date":
                return TryDate(value, text, out var d) ? d.ToString("dd MMM yyyy", Inv) : text;
            case "datetime":
                return TryDate(value, text, out var dt) ? dt.ToString("dd MMM yyyy HH:mm", Inv) : text;
            default:
                return text;
        }
    }

    private static bool TryDate(object value, string text, out DateTime date)
    {
        switch (value)
        {
            case DateTime d: date = d; return true;
            case DateTimeOffset o: date = o.DateTime; return true;
        }

        // Excel keeps dates as serial numbers, and uploaded rows keep them that way.
        if (double.TryParse(text, NumberStyles.Float, Inv, out var serial) && serial > 20000 && serial < 80000)
        {
            date = DateTime.FromOADate(serial);
            return true;
        }

        return DateTime.TryParse(text, Inv, DateTimeStyles.None, out date);
    }

    // -------------------------------------------------------------------------
    // JSON and row helpers
    // -------------------------------------------------------------------------

    private static string Cell(DataRow row, string column) => ApsMetadata.Cell(row, column);

    private static string S(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static int? I(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) &&
        v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n)
            ? n
            : null;

    private static JsonElement Obj(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Object
            ? v
            : default;

    private static List<JsonElement> Arr(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray().ToList()
            : new List<JsonElement>();
}
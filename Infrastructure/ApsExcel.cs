using System.Data;
using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;

namespace AgenticPitstop.Infrastructure;

/// <summary>
/// Minimal .xlsx reader built on System.IO.Compression and System.Xml.Linq.
///
/// An .xlsx file is a zip containing XML parts, so no third-party spreadsheet
/// library is needed — which matters here, because the portal ships with no
/// outside dependencies at all.
///
/// Scope is deliberately narrow: the first worksheet, row 1 as the header, and
/// cell values as strings. That is exactly what template discovery and structured
/// inflow import need. Formulas are read as their cached value; formatting,
/// merged cells and dates-as-numbers are not interpreted.
/// </summary>
public static class ApsExcel
{
    private static readonly XNamespace Main =
        "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace Rel =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PkgRel =
        "http://schemas.openxmlformats.org/package/2006/relationships";

    /// <summary>Header names from row 1 of the first worksheet.</summary>
    public static List<string> ReadHeaders(Stream xlsxStream)
    {
        var table = Read(xlsxStream, maxDataRows: 0);
        return table.Columns.Cast<DataColumn>().Select(c => c.ColumnName).ToList();
    }

    /// <summary>
    /// The header row exactly as the sheet holds it. An empty cell comes back as
    /// an empty string rather than a generated "ColumnN", so the caller can tell
    /// a missing header from a real one - which Read deliberately cannot.
    ///
    /// Trailing empties are dropped: a formatted but unused cell past the last
    /// header is not a column.
    /// </summary>
    public static List<string> ReadRawHeaders(Stream xlsxStream)
    {
        using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Read, leaveOpen: true);

        var sharedStrings = ReadSharedStrings(archive);
        var styles = ReadStyles(archive);
        var sheetPath = ResolveFirstSheetPath(archive);

        var sheetEntry = archive.GetEntry(sheetPath)
            ?? throw new InvalidDataException("The workbook does not contain a readable worksheet.");

        using var sheetStream = sheetEntry.Open();
        var sheet = XDocument.Load(sheetStream);

        var firstRow = sheet.Root?.Element(Main + "sheetData")?.Elements(Main + "row").FirstOrDefault();
        if (firstRow == null) return new List<string>();

        var values = ReadRow(firstRow, sharedStrings, styles)
            .Select(value => (value ?? string.Empty).Trim())
            .ToList();

        while (values.Count > 0 && values[^1].Length == 0)
            values.RemoveAt(values.Count - 1);

        return values;
    }

    /// <summary>
    /// Reads the first worksheet into a DataTable. Row 1 supplies the column
    /// names; every value is returned as a string.
    /// </summary>
    public static DataTable Read(Stream xlsxStream, int maxDataRows = int.MaxValue)
    {
        var table = new DataTable();

        using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Read, leaveOpen: true);

        var sharedStrings = ReadSharedStrings(archive);
        var styles = ReadStyles(archive);
        var sheetPath = ResolveFirstSheetPath(archive);

        var sheetEntry = archive.GetEntry(sheetPath)
            ?? throw new InvalidDataException("The workbook does not contain a readable worksheet.");

        using var sheetStream = sheetEntry.Open();
        var sheet = XDocument.Load(sheetStream);

        var rows = sheet.Root?.Element(Main + "sheetData")?.Elements(Main + "row").ToList()
                   ?? new List<XElement>();

        if (rows.Count == 0) return table;

        // --- header row -------------------------------------------------------
        var headerCells = ReadRow(rows[0], sharedStrings, styles);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < headerCells.Count; index++)
        {
            var name = (headerCells[index] ?? string.Empty).Trim();
            if (name.Length == 0) name = $"Column{index + 1}";

            // Duplicate headers would break DataTable; suffix rather than fail.
            var candidate = name;
            var suffix = 2;
            while (!used.Add(candidate))
            {
                candidate = $"{name}{suffix++}";
            }
            table.Columns.Add(candidate, typeof(string));
        }

        // --- data rows --------------------------------------------------------
        var added = 0;
        for (var rowIndex = 1; rowIndex < rows.Count && added < maxDataRows; rowIndex++)
        {
            var values = ReadRow(rows[rowIndex], sharedStrings, styles);
            if (values.All(string.IsNullOrWhiteSpace)) continue;   // skip blank rows

            var dataRow = table.NewRow();
            for (var column = 0; column < table.Columns.Count; column++)
            {
                dataRow[column] = column < values.Count ? (object)(values[column] ?? string.Empty) : string.Empty;
            }
            table.Rows.Add(dataRow);
            added++;
        }

        return table;
    }

    // -------------------------------------------------------------------------

    private static List<string> ReadSharedStrings(ZipArchive archive)
    {
        var result = new List<string>();
        var entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry == null) return result;

        using var stream = entry.Open();
        var document = XDocument.Load(stream);

        foreach (var item in document.Root?.Elements(Main + "si") ?? Enumerable.Empty<XElement>())
        {
            // <si> is either a single <t>, or several <r><t> runs for rich text.
            var text = string.Concat(item.Descendants(Main + "t").Select(t => t.Value));
            result.Add(text);
        }
        return result;
    }

    /// <summary>
    /// Finds the first worksheet by following workbook.xml -> its relationships,
    /// rather than assuming xl/worksheets/sheet1.xml, which is not guaranteed.
    /// </summary>
    private static string ResolveFirstSheetPath(ZipArchive archive)
    {
        const string fallback = "xl/worksheets/sheet1.xml";

        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        if (workbookEntry == null) return fallback;

        string relationshipId;
        using (var stream = workbookEntry.Open())
        {
            var workbook = XDocument.Load(stream);
            var firstSheet = workbook.Root?
                .Element(Main + "sheets")?
                .Elements(Main + "sheet")
                .FirstOrDefault();

            relationshipId = firstSheet?.Attribute(Rel + "id")?.Value;
        }

        if (string.IsNullOrEmpty(relationshipId)) return fallback;

        var relsEntry = archive.GetEntry("xl/_rels/workbook.xml.rels");
        if (relsEntry == null) return fallback;

        using var relsStream = relsEntry.Open();
        var rels = XDocument.Load(relsStream);

        var target = rels.Root?
            .Elements(PkgRel + "Relationship")
            .FirstOrDefault(r => r.Attribute("Id")?.Value == relationshipId)?
            .Attribute("Target")?.Value;

        if (string.IsNullOrEmpty(target)) return fallback;

        target = target.TrimStart('/');
        return target.StartsWith("xl/", StringComparison.OrdinalIgnoreCase)
            ? target
            : "xl/" + target;
    }

    /// <summary>
    /// Reads one row into a positional list. Cell references are honoured so a
    /// gap in the sheet (B1 present, C1 empty, D1 present) does not shift values
    /// into the wrong column.
    /// </summary>
    private static List<string> ReadRow(XElement row, List<string> sharedStrings, WorkbookStyles styles)
    {
        var values = new List<string>();

        foreach (var cell in row.Elements(Main + "c"))
        {
            var reference = cell.Attribute("r")?.Value;
            var columnIndex = reference == null ? values.Count : ColumnIndex(reference);

            while (values.Count < columnIndex) values.Add(string.Empty);

            values.Add(CellValue(cell, sharedStrings, styles));
        }

        return values;
    }

    private static string CellValue(XElement cell, List<string> sharedStrings, WorkbookStyles styles)
    {
        var type = cell.Attribute("t")?.Value;

        // Inline string
        if (type == "inlineStr")
            return string.Concat(cell.Descendants(Main + "t").Select(t => t.Value));

        var valueElement = cell.Element(Main + "v");
        if (valueElement == null) return string.Empty;

        var raw = valueElement.Value;

        // Shared string table lookup
        if (type == "s" && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index))
            return index >= 0 && index < sharedStrings.Count ? sharedStrings[index] : string.Empty;

        // Boolean
        if (type == "b") return raw == "1" ? "TRUE" : "FALSE";

        // A date cell is a plain number - only its style says otherwise. Converting
        // here keeps the serial out of RowDataJson and out of every agent downstream.
        // A number whose style is not a date is left exactly as it was, so amounts,
        // ages and SLA days are untouched.
        if ((type == null || type == "n") &&
            double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            var styleIndex = 0;
            var styleAttribute = cell.Attribute("s")?.Value;
            if (styleAttribute != null)
                int.TryParse(styleAttribute, NumberStyles.Integer, CultureInfo.InvariantCulture, out styleIndex);

            if (styles.DateStyles.Contains(styleIndex))
            {
                var text = SerialToText(number, styles.Date1904);
                if (text != null) return text;
            }
        }

        return raw;
    }

    /// <summary>
    /// Which cell styles mean "date", and which epoch the workbook counts from.
    /// </summary>
    private sealed class WorkbookStyles
    {
        public HashSet<int> DateStyles { get; } = new();
        public bool Date1904 { get; set; }
    }

    /// <summary>Number formats Excel reserves for dates and times.</summary>
    private static readonly HashSet<int> BuiltInDateFormats = new()
    {
        14, 15, 16, 17, 18, 19, 20, 21, 22,
        27, 28, 29, 30, 31, 32, 33, 34, 35, 36,
        45, 46, 47,
        50, 51, 52, 53, 54, 55, 56, 57, 58
    };

    private static WorkbookStyles ReadStyles(ZipArchive archive)
    {
        var styles = new WorkbookStyles { Date1904 = ReadDate1904(archive) };

        var entry = archive.GetEntry("xl/styles.xml");
        if (entry == null) return styles;

        using var stream = entry.Open();
        var document = XDocument.Load(stream);

        // Custom formats carry their own code; built-in ones are known by id alone.
        var custom = new Dictionary<int, string>();
        foreach (var format in document.Root?.Element(Main + "numFmts")?.Elements(Main + "numFmt")
                               ?? Enumerable.Empty<XElement>())
        {
            if (int.TryParse(format.Attribute("numFmtId")?.Value, NumberStyles.Integer,
                             CultureInfo.InvariantCulture, out var id))
                custom[id] = format.Attribute("formatCode")?.Value ?? string.Empty;
        }

        // cellXfs is positional: a cell's s="4" is the fifth xf in this list.
        var index = 0;
        foreach (var xf in document.Root?.Element(Main + "cellXfs")?.Elements(Main + "xf")
                           ?? Enumerable.Empty<XElement>())
        {
            if (int.TryParse(xf.Attribute("numFmtId")?.Value, NumberStyles.Integer,
                             CultureInfo.InvariantCulture, out var formatId))
            {
                var isDate = BuiltInDateFormats.Contains(formatId)
                          || (custom.TryGetValue(formatId, out var code) && IsDateFormat(code));

                if (isDate) styles.DateStyles.Add(index);
            }
            index++;
        }

        return styles;
    }

    /// <summary>A 1904-based workbook counts days from 1904-01-01, not 1899-12-30.</summary>
    private static bool ReadDate1904(ZipArchive archive)
    {
        var entry = archive.GetEntry("xl/workbook.xml");
        if (entry == null) return false;

        using var stream = entry.Open();
        var document = XDocument.Load(stream);

        var value = document.Root?.Element(Main + "workbookPr")?.Attribute("date1904")?.Value;
        return value == "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A custom format is a date format when, with its literals and its colour or
    /// locale sections removed, a date or time placeholder remains. "0.00" and
    /// "$"#,##0 therefore stay numbers.
    /// </summary>
    private static bool IsDateFormat(string formatCode)
    {
        if (string.IsNullOrWhiteSpace(formatCode)) return false;

        var cleaned = new System.Text.StringBuilder();
        var inQuotes = false;
        var inBrackets = false;

        for (var i = 0; i < formatCode.Length; i++)
        {
            var character = formatCode[i];

            if (inQuotes) { if (character == '"') inQuotes = false; continue; }
            if (inBrackets) { if (character == ']') inBrackets = false; continue; }

            switch (character)
            {
                case '"': inQuotes = true; continue;
                case '[': inBrackets = true; continue;
                case '\\': i++; continue;              // escaped literal
                default: cleaned.Append(character); break;
            }
        }

        foreach (var character in cleaned.ToString())
            if (character is 'y' or 'Y' or 'd' or 'D' or 'h' or 'H'
                           or 's' or 'S' or 'm' or 'M')
                return true;

        return false;
    }

    /// <summary>
    /// ISO text, so the value sorts correctly and parses the same way in SQL,
    /// in the pipeline engine and in any agent that receives it.
    /// </summary>
    private static string SerialToText(double serial, bool date1904)
    {
        try
        {
            var value = date1904
                ? new DateTime(1904, 1, 1).AddDays(serial)
                : DateTime.FromOADate(serial);

            // Below 1 the cell holds a time of day and no date at all.
            if (!date1904 && serial < 1)
                return value.ToString("HH:mm:ss", CultureInfo.InvariantCulture);

            return value.TimeOfDay == TimeSpan.Zero
                ? value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                : value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }
        catch (ArgumentException)
        {
            return null;    // outside the representable range; leave the number alone
        }
    }

    /// <summary>Converts a cell reference such as "AB12" to a zero-based column index.</summary>
    private static int ColumnIndex(string cellReference)
    {
        var index = 0;
        foreach (var character in cellReference)
        {
            if (character is >= 'A' and <= 'Z')
                index = index * 26 + (character - 'A' + 1);
            else if (character is >= 'a' and <= 'z')
                index = index * 26 + (character - 'a' + 1);
            else
                break;
        }
        return Math.Max(0, index - 1);
    }
}

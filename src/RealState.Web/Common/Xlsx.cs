using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;

namespace RealState.Web.Common;

/// <summary>
/// Builds a styled .xlsx download: a light-blue header row, alternating white / light-gray data rows,
/// and an optional bold totals row. The sheet is right-to-left so columns read like the on-screen table.
/// Numeric cells (int/long/decimal/double) are written as real numbers; everything else as text.
/// </summary>
public static class Xlsx
{
    private static readonly XLColor HeaderFill = XLColor.FromHtml("#BDD7EE"); // light blue
    private static readonly XLColor RowAltFill = XLColor.FromHtml("#F2F2F2"); // light gray
    private static readonly XLColor TotalFill = XLColor.FromHtml("#DCE6F1");   // pale blue
    private static readonly XLColor GridColor = XLColor.FromHtml("#D9D9D9");

    public static FileContentResult File(
        string fileName, string sheetName,
        IReadOnlyList<string> headers,
        IEnumerable<IReadOnlyList<object?>> rows,
        IReadOnlyList<object?>? totals = null)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet(string.IsNullOrWhiteSpace(sheetName) ? "Sheet1" : Trim(sheetName));
        ws.RightToLeft = true;

        // Header
        for (var c = 0; c < headers.Count; c++)
        {
            var cell = ws.Cell(1, c + 1);
            cell.Value = headers[c];
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontColor = XLColor.FromHtml("#1F3864");
            cell.Style.Fill.BackgroundColor = HeaderFill;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            cell.Style.Border.OutsideBorderColor = GridColor;
        }

        // Data
        var r = 2;
        foreach (var row in rows)
        {
            var alt = (r % 2) == 1;   // r=2 (first data row) -> white, r=3 -> gray, ...
            for (var c = 0; c < row.Count; c++)
            {
                var cell = ws.Cell(r, c + 1);
                SetValue(cell, row[c]);
                cell.Style.Fill.BackgroundColor = alt ? RowAltFill : XLColor.White;
                cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                cell.Style.Border.OutsideBorderColor = GridColor;
            }
            r++;
        }

        // Totals
        if (totals is { Count: > 0 })
        {
            for (var c = 0; c < totals.Count; c++)
            {
                var cell = ws.Cell(r, c + 1);
                SetValue(cell, totals[c]);
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = TotalFill;
                cell.Style.Border.TopBorder = XLBorderStyleValues.Medium;
                cell.Style.Border.TopBorderColor = XLColor.FromHtml("#8EAADB");
            }
        }

        ws.SheetView.FreezeRows(1);
        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return new FileContentResult(ms.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")
        { FileDownloadName = fileName };
    }

    private static void SetValue(IXLCell cell, object? v)
    {
        switch (v)
        {
            case null:
                cell.Value = string.Empty;
                break;
            case decimal d:
                cell.Value = d; cell.Style.NumberFormat.Format = "#,##0.##";
                break;
            case double db:
                cell.Value = db; cell.Style.NumberFormat.Format = "#,##0.##";
                break;
            case int i:
                cell.Value = i; cell.Style.NumberFormat.Format = "#,##0";
                break;
            case long l:
                cell.Value = l; cell.Style.NumberFormat.Format = "#,##0";
                break;
            default:
                cell.Value = v.ToString();
                break;
        }
    }

    // Worksheet names can't exceed 31 chars or contain : \ / ? * [ ].
    private static string Trim(string name)
    {
        foreach (var ch in new[] { ':', '\\', '/', '?', '*', '[', ']' }) name = name.Replace(ch, ' ');
        return name.Length > 31 ? name[..31] : name;
    }
}

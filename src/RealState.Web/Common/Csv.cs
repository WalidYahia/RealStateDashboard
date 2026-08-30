using System.Text;
using Microsoft.AspNetCore.Mvc;

namespace RealState.Web.Common;

/// <summary>
/// Builds a downloadable CSV file. Prepends a UTF-8 BOM so Excel opens Arabic text correctly, and
/// quotes/escapes fields per RFC 4180.
/// </summary>
public static class Csv
{
    public static FileContentResult File(string fileName, IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string?>> rows)
    {
        var sb = new StringBuilder();
        sb.Append(string.Join(",", headers.Select(Escape))).Append("\r\n");
        foreach (var row in rows)
            sb.Append(string.Join(",", row.Select(Escape))).Append("\r\n");

        var bom = Encoding.UTF8.GetPreamble();
        var body = Encoding.UTF8.GetBytes(sb.ToString());
        var bytes = new byte[bom.Length + body.Length];
        Buffer.BlockCopy(bom, 0, bytes, 0, bom.Length);
        Buffer.BlockCopy(body, 0, bytes, bom.Length, body.Length);

        return new FileContentResult(bytes, "text/csv; charset=utf-8") { FileDownloadName = fileName };
    }

    private static string Escape(string? field)
    {
        var s = field ?? string.Empty;
        if (s.IndexOfAny(new[] { '"', ',', '\n', '\r' }) >= 0)
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        return s;
    }
}

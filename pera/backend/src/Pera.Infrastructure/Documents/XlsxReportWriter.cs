using ClosedXML.Excel;
using Pera.Application.Dtos;
using Pera.Application.Interfaces;

namespace Pera.Infrastructure.Documents;

/// <summary>Excel export of any report: a header block with the report date and applied filters, then a filterable table.</summary>
public sealed class XlsxReportWriter : IReportWriter
{
    public byte[] Xlsx(ReportResult r)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(r.Key.Length > 31 ? r.Key[..31] : r.Key);
        ws.Cell(1, 1).Value = "PERA — " + r.Title; ws.Cell(1, 1).Style.Font.Bold = true; ws.Cell(1, 1).Style.Font.FontSize = 14;
        ws.Cell(2, 1).Value = $"Generated {r.GeneratedAt:dd-MM-yyyy HH:mm} UTC by {r.GeneratedBy}";
        ws.Cell(3, 1).Value = "Filters: " + string.Join("; ", r.Filters.Select(kv => $"{kv.Key}: {kv.Value}"));
        ws.Cell(4, 1).Value = $"Rows: {r.Rows.Count}";
        const int head = 6;
        for (var c = 0; c < r.Columns.Count; c++) ws.Cell(head, c + 1).Value = r.Columns[c].Label;
        for (var i = 0; i < r.Rows.Count; i++)
            for (var c = 0; c < r.Columns.Count; c++)
            {
                var cell = ws.Cell(head + 1 + i, c + 1);
                var col = r.Columns[c];
                switch (r.Rows[i].GetValueOrDefault(col.Key))
                {
                    case null: break;
                    case DateTime d: cell.Value = d; cell.Style.DateFormat.Format = col.Kind == "date" ? "dd-mm-yyyy" : "dd-mm-yyyy hh:mm"; break;
                    case int n: cell.Value = n; break;
                    case long n: cell.Value = n; break;
                    case double n: cell.Value = n; break;
                    case decimal n: cell.Value = n; break;
                    case var v: cell.Value = v.ToString(); break;
                }
            }
        if (r.Rows.Count > 0) ws.Range(head, 1, head + r.Rows.Count, r.Columns.Count).CreateTable("Report");
        else ws.Range(head, 1, head, r.Columns.Count).Style.Font.Bold = true;
        ws.Columns().AdjustToContents(8, 60);
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }
}

using ClosedXML.Excel;

namespace GeoAssets.Provider.Xlsx;

/// <summary>ClosedXML-backed implementation of <see cref="IWorkbookReader"/>.</summary>
public sealed class ClosedXmlWorkbookReader : IWorkbookReader
{
    public IReadOnlyList<IReadOnlyList<string?>> ReadRows(Stream workbookStream, string? worksheetName = null)
    {
        using var workbook  = new XLWorkbook(workbookStream);
        var worksheet       = worksheetName is null
            ? workbook.Worksheets.First()
            : workbook.Worksheet(worksheetName);

        var usedRange = worksheet.RangeUsed();
        if (usedRange is null) return [];

        var columnCount = usedRange.ColumnCount();
        var rows        = new List<IReadOnlyList<string?>>(usedRange.RowCount());

        foreach (var row in usedRange.RowsUsed())
        {
            var cells = new string?[columnCount];
            for (var i = 0; i < columnCount; i++)
            {
                var cell = row.Cell(i + 1);
                cells[i] = cell.IsEmpty() ? null : cell.GetString();
            }

            rows.Add(cells);
        }

        return rows;
    }
}

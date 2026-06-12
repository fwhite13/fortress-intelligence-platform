using ClosedXML.Excel;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace FortressAI.Web.Services;

public interface IXlsxPresizerService
{
    /// <summary>
    /// Reads an XLSX stream, applies LibreOffice-compatible page sizing to each worksheet,
    /// and returns the presized bytes along with the worksheet names (chart sheets excluded).
    /// </summary>
    Task<XlsxPresizeResult> PresizeAsync(Stream xlsxStream);
}

public record XlsxPresizeResult(byte[] Bytes, string[] SheetNames);

/// <summary>
/// Presizes XLSX workbooks for LibreOffice PDF conversion.
/// Ports the ExcelJS presizeWorkbook() logic from pptx-converter/server.js to C#.
///
/// Two-pass approach:
/// Pass 1 (ClosedXML): Measure column widths + row heights per worksheet; collect sheet names.
///   ClosedXML.Worksheets automatically excludes chart sheets — no guard code needed.
/// Pass 2 (OpenXML SDK): Inject paperWidth/paperHeight as SetAttribute() calls.
///   These are non-standard OOXML attributes (LibreOffice extension). Cannot use standard
///   ClosedXML PageSetup because it doesn't expose these non-standard fields. SetAttribute()
///   avoids corrupting the surrounding standard OOXML — same risk profile as the prior ExcelJS approach.
///   CRITICAL: Do NOT set Orientation — LibreOffice reads paperWidth/paperHeight literally as X/Y.
/// </summary>
public class XlsxPresizerService : IXlsxPresizerService
{
    // Constants — must match pptx-converter/server.js exactly
    private const double DefaultColWidthChars = 8.43;
    private const double DefaultRowHeightPt   = 15.0;
    private const double CharsToMm            = 2.1;
    private const double PtToMm               = 0.3528;
    private const double MarginMm             = 15.0;

    private readonly ILogger<XlsxPresizerService> _logger;

    public XlsxPresizerService(ILogger<XlsxPresizerService> logger)
    {
        _logger = logger;
    }

    public Task<XlsxPresizeResult> PresizeAsync(Stream xlsxStream)
    {
        // Pass 1: ClosedXML — measure dimensions and collect sheet names
        // ClosedXML.Worksheets auto-excludes chart sheets; no special handling needed
        var dimensions = new Dictionary<string, (double widthMm, double heightMm)>();
        var sheetNames = new List<string>();

        _logger.LogInformation("[xlsx-presize] Starting ClosedXML measurement pass");

        using var wb = new XLWorkbook(xlsxStream);

        foreach (var ws in wb.Worksheets)
        {
            sheetNames.Add(ws.Name);

            var columnsUsed = ws.ColumnsUsed().ToList();
            double totalColMm = columnsUsed.Any()
                ? columnsUsed.Sum(col => (col.Width > 0 ? col.Width : DefaultColWidthChars) * CharsToMm)
                : DefaultColWidthChars * CharsToMm; // fallback for empty sheets

            var rowsUsed = ws.RowsUsed().ToList();
            double totalRowMm = rowsUsed.Any()
                ? rowsUsed.Sum(row => (row.Height > 0 ? row.Height : DefaultRowHeightPt) * PtToMm)
                : DefaultRowHeightPt * PtToMm; // fallback for empty sheets

            double widthMm  = Math.Max(totalColMm + MarginMm * 2, 210.0);
            double heightMm = Math.Max(totalRowMm + MarginMm * 2, 297.0);

            dimensions[ws.Name] = (widthMm, heightMm);
            _logger.LogInformation("[xlsx-presize] Sheet '{SheetName}': {WidthMm:F0}mm x {HeightMm:F0}mm",
                ws.Name, widthMm, heightMm);
        }

        _logger.LogInformation("[xlsx-presize] Measured {Count} worksheet(s): {Names}",
            sheetNames.Count, string.Join(", ", sheetNames));

        // Save ClosedXML workbook to a new MemoryStream
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        ms.Position = 0;

        // Pass 2: OpenXML SDK — inject paperWidth/paperHeight attributes
        // Using SetAttribute() for non-standard LibreOffice extension attributes
        _logger.LogInformation("[xlsx-presize] Starting OpenXML SDK page-setup injection pass");

        using var oxDoc = SpreadsheetDocument.Open(ms, isEditable: true);
        var workbookPart = oxDoc.WorkbookPart!;

        // Build a map: relationshipId → sheet name (from the workbook's Sheets collection)
        var sheetIdToName = new Dictionary<string, string>();
        var workbookSheets = workbookPart.Workbook.Sheets;
        if (workbookSheets != null)
        {
            foreach (var sheetElem in workbookSheets.Elements<Sheet>())
            {
                if (sheetElem.Id?.Value != null && sheetElem.Name?.Value != null)
                    sheetIdToName[sheetElem.Id.Value] = sheetElem.Name.Value;
            }
        }

        foreach (var worksheetPart in workbookPart.WorksheetParts)
        {
            // Find the relationship ID for this worksheet part
            var relId = workbookPart.GetIdOfPart(worksheetPart);
            if (!sheetIdToName.TryGetValue(relId, out var sheetName))
            {
                _logger.LogDebug("[xlsx-presize] WorksheetPart relId={RelId} not found in sheet name map — skipping", relId);
                continue;
            }

            if (!dimensions.TryGetValue(sheetName, out var dim))
            {
                _logger.LogDebug("[xlsx-presize] No dimensions for sheet '{SheetName}' — skipping page setup", sheetName);
                continue;
            }

            var worksheet = worksheetPart.Worksheet;

            // Get or create PageSetup element
            var pageSetup = worksheet.Descendants<PageSetup>().FirstOrDefault();
            if (pageSetup == null)
            {
                pageSetup = new PageSetup();
                worksheet.AppendChild(pageSetup);
            }

            // Inject non-standard LibreOffice extension attributes for page dimensions
            // CRITICAL: Do NOT set Orientation — LibreOffice reads paperWidth/paperHeight literally as X/Y
            pageSetup.SetAttribute(new OpenXmlAttribute("paperWidth",  null, $"{Math.Round(dim.widthMm)}mm"));
            pageSetup.SetAttribute(new OpenXmlAttribute("paperHeight", null, $"{Math.Round(dim.heightMm)}mm"));
            pageSetup.FitToWidth = 1;
            pageSetup.FitToHeight = 1;

            // FitToPage lives on SheetProperties.PageSetupProperties, not on PageSetup
            var sheetProps = worksheet.GetFirstChild<SheetProperties>() ?? new SheetProperties();
            if (worksheet.GetFirstChild<SheetProperties>() == null)
                worksheet.InsertAt(sheetProps, 0);
            sheetProps.PageSetupProperties ??= new PageSetupProperties();
            sheetProps.PageSetupProperties.FitToPage = true;

            _logger.LogInformation("[xlsx-presize] Sheet '{SheetName}': injected paperWidth={W}mm paperHeight={H}mm",
                sheetName, Math.Round(dim.widthMm), Math.Round(dim.heightMm));
        }

        oxDoc.Save();
        var resultBytes = ms.ToArray();

        _logger.LogInformation("[xlsx-presize] Complete — {Bytes} bytes, {SheetCount} sheet(s)",
            resultBytes.Length, sheetNames.Count);

        return Task.FromResult(new XlsxPresizeResult(resultBytes, sheetNames.ToArray()));
    }
}

using Amazon.S3;
using Amazon.S3.Model;
using ClosedXML.Excel;

namespace FortressAI.Web.Services;

// Request/result models
public record XlsxGenerationResult(Guid ArtifactId, string S3Key);

public record XlsxGenerationRequest(
    string Title,
    List<XlsxSheet> Sheets,
    XlsxPivotConfig? Pivot
);

public record XlsxSheet(
    string Name,
    List<string> Columns,
    List<List<object>> Rows
);

public record XlsxPivotConfig(
    string SourceSheet,
    string PivotSheetName,
    List<string> RowLabels,
    List<string> ColumnLabels,
    string ValueField,
    string SummaryFormula,   // "Sum", "Count", "Average", "Max", "Min"
    List<string>? ReportFilters
);

public interface IXlsxGenerationService
{
    Task<XlsxGenerationResult> GenerateAsync(XlsxGenerationRequest request, string userId);
}

/// <summary>
/// Generates XLSX workbooks using ClosedXML 0.105.
/// Supports plain data sheets and optional interactive pivot tables.
///
/// Caveats:
/// 1. Pivot projected values are NOT pre-computed by ClosedXML — Excel recomputes on open.
///    Non-Excel viewers (LibreOffice, Google Sheets) show a blank pivot sheet. Acceptable for FAIT
///    since we target Excel users.
/// 2. ClosedXML pivot tables are known to corrupt above ~32,895 source rows (GitHub issue #1182).
///    Not a practical FAIT constraint but worth noting.
/// </summary>
public class XlsxGenerationService : IXlsxGenerationService
{
    private readonly IAmazonS3 _s3Client;
    private readonly IConfiguration _config;
    private readonly ILogger<XlsxGenerationService> _logger;
    private readonly string _bucket;

    public XlsxGenerationService(IAmazonS3 s3Client, IConfiguration config, ILogger<XlsxGenerationService> logger)
    {
        _s3Client = s3Client;
        _config = config;
        _logger = logger;
        _bucket = config["WORKSPACE_S3_BUCKET"] ?? "fortress-user-workspaces";
    }

    public async Task<XlsxGenerationResult> GenerateAsync(XlsxGenerationRequest request, string userId)
    {
        if (request.Sheets == null || request.Sheets.Count == 0)
            throw new ArgumentException("sheets is required and must not be empty");

        _logger.LogInformation("[xlsx-gen] Generating workbook '{Title}' for user {UserId} — {SheetCount} sheet(s), pivot={HasPivot}",
            request.Title, userId, request.Sheets.Count, request.Pivot != null);

        using var wb = new XLWorkbook();

        // Build data sheets
        IXLWorksheet? sourceSheet = null;
        int lastDataRow = 0;

        foreach (var sheet in request.Sheets)
        {
            _logger.LogInformation("[xlsx-gen] Adding sheet '{SheetName}' with {ColCount} columns, {RowCount} rows",
                sheet.Name, sheet.Columns.Count, sheet.Rows.Count);

            var ws = wb.AddWorksheet(sheet.Name);

            // Write headers
            for (int c = 0; c < sheet.Columns.Count; c++)
                ws.Cell(1, c + 1).Value = sheet.Columns[c];

            // Write rows
            for (int r = 0; r < sheet.Rows.Count; r++)
            {
                for (int c = 0; c < sheet.Rows[r].Count; c++)
                {
                    var cell = ws.Cell(r + 2, c + 1);
                    var val = sheet.Rows[r][c];
                    if (val is System.Text.Json.JsonElement je)
                    {
                        // Deserialize from JSON element
                        switch (je.ValueKind)
                        {
                            case System.Text.Json.JsonValueKind.Number:
                                cell.Value = je.GetDouble();
                                break;
                            case System.Text.Json.JsonValueKind.True:
                                cell.Value = true;
                                break;
                            case System.Text.Json.JsonValueKind.False:
                                cell.Value = false;
                                break;
                            default:
                                cell.Value = je.GetString() ?? string.Empty;
                                break;
                        }
                    }
                    else if (val is double d) cell.Value = d;
                    else if (val is int i) cell.Value = i;
                    else if (val is long l) cell.Value = l;
                    else if (val is decimal dec) cell.Value = (double)dec;
                    else if (double.TryParse(val?.ToString(), System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out var parsed))
                        cell.Value = parsed;
                    else cell.Value = val?.ToString() ?? string.Empty;
                }
            }

            if (request.Pivot?.SourceSheet == sheet.Name)
            {
                sourceSheet = ws;
                lastDataRow = sheet.Rows.Count + 1; // +1 for header row
            }
        }

        // Build pivot (optional)
        if (request.Pivot != null && sourceSheet != null)
        {
            _logger.LogInformation("[xlsx-gen] Adding pivot table '{PivotSheet}' from source '{SourceSheet}'",
                request.Pivot.PivotSheetName, request.Pivot.SourceSheet);

            var pivot = request.Pivot;
            var colCount = sourceSheet.ColumnsUsed().Count();
            var sourceRange = sourceSheet.Range(1, 1, lastDataRow, colCount);
            var pivotWs = wb.AddWorksheet(pivot.PivotSheetName);
            var pt = pivotWs.PivotTables.Add("Pivot", pivotWs.Cell("A1"), sourceRange);

            foreach (var rowLabel in pivot.RowLabels)
                pt.RowLabels.Add(rowLabel);

            foreach (var colLabel in pivot.ColumnLabels)
                pt.ColumnLabels.Add(colLabel);

            var summaryFormula = pivot.SummaryFormula?.ToLower() switch
            {
                "count"   => XLPivotSummary.Count,
                "average" => XLPivotSummary.Average,
                "max"     => XLPivotSummary.Maximum,
                "min"     => XLPivotSummary.Minimum,
                _         => XLPivotSummary.Sum
            };
            pt.Values.Add(pivot.ValueField).SetSummaryFormula(summaryFormula);

            if (pivot.ReportFilters != null)
                foreach (var filter in pivot.ReportFilters)
                    pt.ReportFilters.Add(filter);

            pt.PivotCache.RefreshDataOnOpen = true;
            pt.PivotCache.SaveSourceData = true;
        }
        else if (request.Pivot != null && sourceSheet == null)
        {
            _logger.LogWarning("[xlsx-gen] Pivot config specified sourceSheet '{SourceSheet}' but no matching sheet was found — pivot skipped",
                request.Pivot.SourceSheet);
        }

        // Save to stream → S3 upload
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        ms.Position = 0;

        var artifactId = Guid.NewGuid();
        var s3Key = $"artifacts/{userId}/{artifactId}.xlsx";

        _logger.LogInformation("[xlsx-gen] Uploading workbook to s3://{Bucket}/{Key}", _bucket, s3Key);

        await _s3Client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _bucket,
            Key = s3Key,
            InputStream = ms,
            ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
        });

        _logger.LogInformation("[xlsx-gen] Done — artifactId={ArtifactId} s3Key={S3Key}", artifactId, s3Key);
        return new XlsxGenerationResult(artifactId, s3Key);
    }
}

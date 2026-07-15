using Markdig;
using Markdig.Extensions.Tables;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Text;

namespace FortressIntelligenceRM.Web.Services;

/// <summary>
/// Converts a markdown string to a clean PDF using QuestPDF.
/// Parses the markdown into block elements (headings, paragraphs, bullets, tables)
/// and renders them with appropriate typography.
/// </summary>
public class PdfService
{
    public byte[] BuildPdf(string markdownText, string title, string primaryColor = "#1a2332")
    {
        QuestPDF.Settings.License = LicenseType.Community;

        // Normalize color: strip leading '#' for QuestPDF's Color.FromHex
        var brandHex = primaryColor.TrimStart('#');

        var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().DisableHtml().Build();
        var doc = Markdig.Markdown.Parse(markdownText, pipeline);

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(40);
                page.DefaultTextStyle(x => x.FontSize(11).FontFamily(Fonts.Arial));

                page.Header().PaddingBottom(8).BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Column(col =>
                {
                    col.Item().Text(title).FontSize(14).Bold().FontColor(Color.FromHex(brandHex));
                    col.Item().Text($"Generated {DateTime.UtcNow:yyyy-MM-dd}").FontSize(9).FontColor(Colors.Grey.Darken1);
                });

                page.Content().PaddingTop(12).Column(col =>
                {
                    foreach (var block in doc)
                    {
                        switch (block)
                        {
                            case Markdig.Syntax.HeadingBlock h:
                            {
                                var text = ExtractInlineText(h.Inline);
                                var (size, bold) = h.Level switch
                                {
                                    1 => (16f, true),
                                    2 => (13f, true),
                                    _ => (11f, true)
                                };
                                col.Item().PaddingTop(h.Level <= 2 ? 14 : 8).PaddingBottom(4)
                                    .Text(text).FontSize(size).Bold().FontColor(Color.FromHex(brandHex));
                                break;
                            }
                            case Markdig.Syntax.ParagraphBlock p:
                            {
                                col.Item().PaddingBottom(6).Text(txt =>
                                {
                                    RenderInlines(txt, p.Inline);
                                });
                                break;
                            }
                            case Markdig.Syntax.ListBlock list:
                            {
                                foreach (var listItem in list.OfType<Markdig.Syntax.ListItemBlock>())
                                {
                                    foreach (var child in listItem.OfType<Markdig.Syntax.ParagraphBlock>())
                                    {
                                        col.Item().PaddingLeft(16).PaddingBottom(3).Row(row =>
                                        {
                                            row.ConstantItem(12).Text("•").FontColor(Color.FromHex(brandHex));
                                            row.RelativeItem().Text(txt =>
                                            {
                                                RenderInlines(txt, child.Inline);
                                            });
                                        });
                                    }
                                }
                                col.Item().PaddingBottom(4);
                                break;
                            }
                            case Markdig.Syntax.ThematicBreakBlock:
                                col.Item().PaddingVertical(6).LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten2);
                                break;
                            case Table mdTable:
                            {
                                var rows = mdTable.OfType<TableRow>().ToList();
                                if (!rows.Any()) break;
                                var colCount = rows.Max(r => r.Count);
                                if (colCount == 0) break;
                                col.Item().PaddingBottom(8).Table(tbl =>
                                {
                                    tbl.ColumnsDefinition(cols =>
                                    {
                                        for (int c = 0; c < colCount; c++)
                                            cols.RelativeColumn();
                                    });
                                    foreach (var row in rows)
                                    {
                                        var isHeader = row.IsHeader;
                                        foreach (var cell in row.OfType<TableCell>())
                                        {
                                            var cellText = string.Join(" ",
                                                cell.OfType<Markdig.Syntax.ParagraphBlock>()
                                                    .Select(p => ExtractInlineText(p.Inline)));
                                            var cellItem = tbl.Cell()
                                                .Border(0.5f).BorderColor(Colors.Grey.Lighten2)
                                                .Background(isHeader ? Colors.Grey.Lighten4 : Colors.White)
                                                .Padding(5);
                                            if (isHeader)
                                                cellItem.Text(cellText).FontSize(10).Bold().FontColor(Color.FromHex(brandHex));
                                            else
                                                cellItem.Text(cellText).FontSize(10).FontColor(Colors.Grey.Darken3);
                                        }
                                    }
                                });
                                break;
                            }
                        }
                    }
                });

                page.Footer().AlignCenter().Text(txt =>
                {
                    txt.Span("Page ").FontSize(9).FontColor(Colors.Grey.Darken1);
                    txt.CurrentPageNumber().FontSize(9).FontColor(Colors.Grey.Darken1);
                    txt.Span(" of ").FontSize(9).FontColor(Colors.Grey.Darken1);
                    txt.TotalPages().FontSize(9).FontColor(Colors.Grey.Darken1);
                });
            });
        }).GeneratePdf();
    }

    private static string ExtractInlineText(Markdig.Syntax.Inlines.ContainerInline? inlines)
    {
        if (inlines == null) return string.Empty;
        var sb = new StringBuilder();
        foreach (var inline in inlines)
        {
            if (inline is Markdig.Syntax.Inlines.LiteralInline lit) sb.Append(lit.Content.ToString());
            else if (inline is Markdig.Syntax.Inlines.EmphasisInline emp) sb.Append(ExtractInlineText(emp));
            else if (inline is Markdig.Syntax.Inlines.ContainerInline ci) sb.Append(ExtractInlineText(ci));
        }
        return sb.ToString();
    }

    private static void RenderInlines(TextDescriptor txt, Markdig.Syntax.Inlines.ContainerInline? inlines)
    {
        if (inlines == null) return;
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case Markdig.Syntax.Inlines.LiteralInline lit:
                    txt.Span(lit.Content.ToString());
                    break;
                case Markdig.Syntax.Inlines.EmphasisInline emp:
                    var empText = ExtractInlineText(emp);
                    if (emp.DelimiterCount == 2) txt.Span(empText).Bold();
                    else txt.Span(empText).Italic();
                    break;
                case Markdig.Syntax.Inlines.LineBreakInline:
                    txt.Span("\n");
                    break;
                case Markdig.Syntax.Inlines.ContainerInline ci:
                    RenderInlines(txt, ci);
                    break;
            }
        }
    }
}

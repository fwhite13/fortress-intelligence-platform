using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace FortressAI.Web.Services;

/// <summary>
/// Real Word document generator using OpenXml SDK.
/// Produces: Cover page → TOC → Sections → Page-number footer.
/// Thread-safe singleton.
/// </summary>
public class WordDocumentGenerator : IDocumentGeneratorService
{
    public Task<byte[]> GenerateAsync(DocumentGenerationRequest request, CancellationToken ct = default)
    {
        using var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document, true))
        {
            var mainPart = doc.AddMainDocumentPart();

            // Embed styles
            AddStylesPart(mainPart);

            // Build body
            var body = new Body();

            // Cover page
            body.AppendChild(CreateTitleParagraph(request.Title));
            body.AppendChild(CreateTimestampParagraph());
            body.AppendChild(CreatePageBreak());

            // TOC page
            body.AppendChild(CreateTocField());
            body.AppendChild(CreatePageBreak());

            // Sections
            foreach (var section in request.Sections)
            {
                body.AppendChild(CreateHeading1(section.Heading));
                foreach (var para in ParseSectionContent(section.Content))
                    body.AppendChild(para);
            }

            // SectionProperties (required at end of body)
            body.AppendChild(new SectionProperties());

            mainPart.Document = new Document(body);
            mainPart.Document.Save();

            // Settings: w:updateFields
            AddUpdateFieldsSetting(mainPart);

            // Footer: page numbers
            AddPageNumberFooter(mainPart);
        }

        return Task.FromResult(ms.ToArray());
    }

    // ─── Styles ────────────────────────────────────────────────────────────────

    private static void AddStylesPart(MainDocumentPart mainPart)
    {
        var stylesPart = mainPart.AddNewPart<StyleDefinitionsPart>();
        stylesPart.Styles = new Styles(
            CreateNormalStyle(),
            CreateTitleStyle(),
            CreateHeading1Style(),
            CreateHeading2Style()
        );
        stylesPart.Styles.Save();
    }

    private static Style CreateNormalStyle()
    {
        var style = new Style
        {
            Type = StyleValues.Paragraph,
            StyleId = "Normal",
            Default = true,
        };
        style.AppendChild(new StyleName { Val = "Normal" });
        style.AppendChild(new StyleParagraphProperties(
            new SpacingBetweenLines { After = "160", Line = "276", LineRule = LineSpacingRuleValues.Auto }
        ));
        return style;
    }

    private static Style CreateTitleStyle()
    {
        var style = new Style
        {
            Type = StyleValues.Paragraph,
            StyleId = "Title",
        };
        style.AppendChild(new StyleName { Val = "Title" });
        style.AppendChild(new BasedOn { Val = "Normal" });
        style.AppendChild(new StyleRunProperties(
            new Bold(),
            new FontSize { Val = "52" }
        ));
        return style;
    }

    private static Style CreateHeading1Style()
    {
        var style = new Style
        {
            Type = StyleValues.Paragraph,
            StyleId = "Heading1",
        };
        style.AppendChild(new StyleName { Val = "heading 1" });
        style.AppendChild(new BasedOn { Val = "Normal" });
        style.AppendChild(new NextParagraphStyle { Val = "Normal" });
        style.AppendChild(new StyleParagraphProperties(
            new OutlineLevel { Val = 0 }
        ));
        style.AppendChild(new StyleRunProperties(
            new Bold(),
            new FontSize { Val = "32" },
            new Color { Val = "2E74B5" }
        ));
        return style;
    }

    private static Style CreateHeading2Style()
    {
        var style = new Style
        {
            Type = StyleValues.Paragraph,
            StyleId = "Heading2",
        };
        style.AppendChild(new StyleName { Val = "heading 2" });
        style.AppendChild(new BasedOn { Val = "Normal" });
        style.AppendChild(new NextParagraphStyle { Val = "Normal" });
        style.AppendChild(new StyleParagraphProperties(
            new OutlineLevel { Val = 1 }
        ));
        style.AppendChild(new StyleRunProperties(
            new Bold(),
            new FontSize { Val = "26" },
            new Color { Val = "2E74B5" }
        ));
        return style;
    }

    // ─── Cover Page ─────────────────────────────────────────────────────────────

    private static Paragraph CreateTitleParagraph(string title)
    {
        var para = new Paragraph();
        para.AppendChild(new ParagraphProperties(
            new ParagraphStyleId { Val = "Title" }
        ));
        para.AppendChild(new Run(new Text(title)));
        return para;
    }

    private static Paragraph CreateTimestampParagraph()
    {
        var timestamp = DateTime.UtcNow.ToString("MMMM d, yyyy");
        var para = new Paragraph();
        para.AppendChild(new Run(new Text($"Generated: {timestamp}")));
        return para;
    }

    private static Paragraph CreatePageBreak()
    {
        var para = new Paragraph();
        para.AppendChild(new Run(new Break { Type = BreakValues.Page }));
        return para;
    }

    // ─── TOC ─────────────────────────────────────────────────────────────────────

    private static Paragraph CreateTocField()
    {
        return new Paragraph(
            new Run(new FieldChar { FieldCharType = FieldCharValues.Begin }),
            new Run(new FieldCode(" TOC \\h \\z \\u ")),
            new Run(new FieldChar { FieldCharType = FieldCharValues.Separate }),
            new Run(new Text("[Right-click → Update Field to refresh table of contents]")),
            new Run(new FieldChar { FieldCharType = FieldCharValues.End })
        );
    }

    // ─── Section Headings ────────────────────────────────────────────────────────

    private static Paragraph CreateHeading1(string text)
    {
        var para = new Paragraph();
        para.AppendChild(new ParagraphProperties(
            new ParagraphStyleId { Val = "Heading1" }
        ));
        para.AppendChild(new Run(new Text(text)));
        return para;
    }

    // ─── Content Parsing ─────────────────────────────────────────────────────────

    private static IEnumerable<Paragraph> ParseSectionContent(string content)
    {
        if (string.IsNullOrEmpty(content))
            yield break;

        foreach (var line in content.Split('\n'))
        {
            var para = new Paragraph();
            foreach (var run in ParseInlineContent(line))
                para.AppendChild(run);
            yield return para;
        }
    }

    /// <summary>
    /// Parse inline markdown: **bold** and *italic*.
    /// Emits Run elements with appropriate RunProperties.
    /// No nested formatting required.
    /// </summary>
    private static IEnumerable<Run> ParseInlineContent(string text)
    {
        // Regex: match **bold** or *italic* (non-greedy)
        var pattern = @"\*\*(.+?)\*\*|\*(.+?)\*";
        var lastIndex = 0;

        foreach (Match match in Regex.Matches(text, pattern))
        {
            // Plain text before match
            if (match.Index > lastIndex)
            {
                var plain = text[lastIndex..match.Index];
                if (!string.IsNullOrEmpty(plain))
                    yield return new Run(new Text(plain) { Space = SpaceProcessingModeValues.Preserve });
            }

            if (match.Groups[1].Success)
            {
                // **bold**
                var run = new Run(new Text(match.Groups[1].Value) { Space = SpaceProcessingModeValues.Preserve });
                run.PrependChild(new RunProperties(new Bold()));
                yield return run;
            }
            else if (match.Groups[2].Success)
            {
                // *italic*
                var run = new Run(new Text(match.Groups[2].Value) { Space = SpaceProcessingModeValues.Preserve });
                run.PrependChild(new RunProperties(new Italic()));
                yield return run;
            }

            lastIndex = match.Index + match.Length;
        }

        // Remaining plain text
        if (lastIndex < text.Length)
        {
            var remaining = text[lastIndex..];
            if (!string.IsNullOrEmpty(remaining))
                yield return new Run(new Text(remaining) { Space = SpaceProcessingModeValues.Preserve });
        }
    }

    // ─── Settings ────────────────────────────────────────────────────────────────

    private static void AddUpdateFieldsSetting(MainDocumentPart mainPart)
    {
        var settingsPart = mainPart.AddNewPart<DocumentSettingsPart>();
        settingsPart.Settings = new Settings(
            new UpdateFieldsOnOpen { Val = true }
        );
        settingsPart.Settings.Save();
    }

    // ─── Footer ──────────────────────────────────────────────────────────────────

    private static void AddPageNumberFooter(MainDocumentPart mainPart)
    {
        var footerPart = mainPart.AddNewPart<FooterPart>();
        footerPart.Footer = new Footer(
            new Paragraph(
                new ParagraphProperties(
                    new Justification { Val = JustificationValues.Right }
                ),
                new Run(new FieldChar { FieldCharType = FieldCharValues.Begin }),
                new Run(new FieldCode(" PAGE ")),
                new Run(new FieldChar { FieldCharType = FieldCharValues.End })
            )
        );
        footerPart.Footer.Save();

        // Wire footer to SectionProperties
        var footerRef = new FooterReference
        {
            Type = HeaderFooterValues.Default,
            Id = mainPart.GetIdOfPart(footerPart)
        };

        var sectPr = mainPart.Document.Body!.GetFirstChild<SectionProperties>()
            ?? new SectionProperties();
        sectPr.AppendChild(footerRef);
    }

    // ─── Table Keep-Together ─────────────────────────────────────────────────────

    /// <summary>
    /// Apply keep-together formatting to a table (port of proposal-generator §3.3 table rules).
    /// Call this on any Table before appending to the document body.
    /// - w:cantSplit on all rows
    /// - w:keepNext + w:keepLines on all rows except last (or ALL rows if ≤3 data rows)
    /// - Header rows get w:tblHeader
    /// </summary>
    private static void ApplyTableKeepTogether(Table table)
    {
        var rows = table.Elements<TableRow>().ToList();
        bool isSmallTable = rows.Count <= 4; // header + ≤3 data rows

        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var trPr = row.GetFirstChild<TableRowProperties>() ?? new TableRowProperties();

            // w:cantSplit on all rows
            trPr.AppendChild(new CantSplit());

            bool isLastRow = i == rows.Count - 1;
            bool applyKeep = !isLastRow || isSmallTable;

            if (applyKeep)
            {
                foreach (var cell in row.Elements<TableCell>())
                {
                    foreach (var para in cell.Elements<Paragraph>())
                    {
                        var pPr = para.GetFirstChild<ParagraphProperties>() ?? new ParagraphProperties();
                        pPr.AppendChild(new KeepNext());
                        pPr.AppendChild(new KeepLines());

                        if (!para.HasChildren || para.GetFirstChild<ParagraphProperties>() == null)
                            para.InsertAt(pPr, 0);
                    }
                }
            }

            if (trPr.HasChildren)
                row.InsertAt(trPr, 0);
        }
    }
}

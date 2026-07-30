using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Groundwork.Models;

namespace Groundwork.Services;

/// <summary>
/// Builds the final .docx report from the synthesised ReportModel using the
/// OpenXML SDK. Written to a MemoryStream so nothing relies on local disk
/// (Render's filesystem is ephemeral).
/// </summary>
public class ReportBuilder
{
    private const string HeadingFont = "Georgia";
    private const string BodyFont = "Calibri";
    private const string Ink = "182A33";
    private const string Teal = "0E655C";
    private const string Grey = "5A6B72";

    public byte[] Build(ReportModel report, List<SourceItem> sources)
    {
        using var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            main.Document = new Document(new Body());
            var body = main.Document.Body!;

            // Title block
            body.Append(Para(report.Title, size: 40, bold: true, color: Ink,
                font: HeadingFont, spaceAfter: 60));
            body.Append(Para($"Desk research briefing · generated {DateTime.UtcNow:d MMMM yyyy}",
                size: 20, color: Grey, spaceAfter: 400));

            // Executive summary
            body.Append(Heading("Executive summary"));
            body.Append(Para(report.ExecutiveSummary, spaceAfter: 300));

            // Themes
            if (report.Themes.Count > 0)
            {
                body.Append(Heading("Themes"));
                foreach (var t in report.Themes)
                {
                    body.Append(Para(t.Heading, size: 26, bold: true, color: Teal,
                        font: HeadingFont, spaceAfter: 80));
                    body.Append(Para(t.Narrative, spaceAfter: 60));
                    AppendSourceLine(main, body, t.SourceUrls);
                }
            }

            // Organisations
            if (report.Organisations.Count > 0)
            {
                body.Append(Heading("Organisations active in this space"));
                foreach (var o in report.Organisations)
                {
                    body.Append(Bullet($"{o.Name} — {o.WhatTheyDo}"));
                    AppendSourceLine(main, body, o.SourceUrls, indented: true);
                }
            }

            // Tools & tech
            if (report.ToolsAndTech.Count > 0)
            {
                body.Append(Heading("Tools and technology in use"));
                foreach (var t in report.ToolsAndTech)
                {
                    body.Append(Bullet($"{t.Name} — {t.Context}"));
                    AppendSourceLine(main, body, t.SourceUrls, indented: true);
                }
            }

            // Datasets
            if (report.Datasets.Count > 0)
            {
                body.Append(Heading("Datasets"));
                foreach (var d in report.Datasets)
                {
                    body.Append(Bullet($"{d.Name} — {d.Details}"));
                    AppendSourceLine(main, body, d.SourceUrls, indented: true);
                }
            }

            // Gaps & opportunities
            if (report.GapsAndOpportunities.Count > 0)
            {
                body.Append(Heading("Gaps and opportunities"));
                foreach (var g in report.GapsAndOpportunities)
                    body.Append(Bullet(g));
            }

            // Next steps
            if (report.SuggestedNextSteps.Count > 0)
            {
                body.Append(Heading("Suggested next steps"));
                foreach (var s in report.SuggestedNextSteps)
                    body.Append(Bullet(s));
            }

            // Sources appendix
            body.Append(Heading("Sources"));
            foreach (var s in sources.Where(s => s.Stage == SourceStage.Extracted))
            {
                var p = new Paragraph(ParaProps(spaceAfter: 60, indent: 240));
                p.Append(Run($"{s.Title} — ", size: 20));
                p.Append(Link(main, s.Url, s.Url));
                body.Append(p);
            }

            main.Document.Save();
        }
        return ms.ToArray();
    }

    // ---- helpers -----------------------------------------------------------

    private static ParagraphProperties ParaProps(int spaceAfter = 160, int indent = 0)
    {
        var props = new ParagraphProperties(
            new SpacingBetweenLines { After = spaceAfter.ToString() });
        if (indent > 0)
            props.Append(new Indentation { Left = indent.ToString() });
        return props;
    }

    private static Run Run(string text, int size = 22, bool bold = false,
        string color = Ink, string font = BodyFont)
    {
        var rp = new RunProperties(
            new RunFonts { Ascii = font, HighAnsi = font },
            new FontSize { Val = size.ToString() },
            new Color { Val = color });
        if (bold) rp.Append(new Bold());
        return new Run(rp, new Text(text) { Space = SpaceProcessingModeValues.Preserve });
    }

    private static Paragraph Para(string text, int size = 22, bool bold = false,
        string color = Ink, string font = BodyFont, int spaceAfter = 160)
        => new(ParaProps(spaceAfter), Run(text, size, bold, color, font));

    private static Paragraph Heading(string text)
        => new(
            new ParagraphProperties(
                new SpacingBetweenLines { Before = "320", After = "160" }),
            Run(text, size: 30, bold: true, color: Ink, font: HeadingFont));

    private static Paragraph Bullet(string text)
        => new(ParaProps(spaceAfter: 80, indent: 240), Run("•  " + text));

    private static Hyperlink Link(MainDocumentPart main, string url, string display)
    {
        HyperlinkRelationship rel;
        try
        {
            rel = main.AddHyperlinkRelationship(new Uri(url), true);
        }
        catch (UriFormatException)
        {
            return new Hyperlink(Run(display, color: Grey));
        }
        var run = Run(display, size: 18, color: Teal);
        run.RunProperties!.Append(new Underline { Val = UnderlineValues.Single });
        return new Hyperlink(run) { Id = rel.Id };
    }

    private static void AppendSourceLine(MainDocumentPart main, Body body,
        List<string> urls, bool indented = false)
    {
        if (urls.Count == 0) return;
        var p = new Paragraph(ParaProps(spaceAfter: 160, indent: indented ? 480 : 240));
        p.Append(Run("Sources: ", size: 18, color: Grey));
        for (var i = 0; i < urls.Count; i++)
        {
            p.Append(Link(main, urls[i], ShortUrl(urls[i])));
            if (i < urls.Count - 1) p.Append(Run("  ·  ", size: 18, color: Grey));
        }
        body.Append(p);
    }

    private static string ShortUrl(string url)
    {
        try { return new Uri(url).Host; }
        catch { return url; }
    }
}

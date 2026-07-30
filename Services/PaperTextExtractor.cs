using System.Text;
using UglyToad.PdfPig;

namespace Groundwork.Services;

/// <summary>
/// Extracts text from an uploaded PDF once, at upload time (GW-HLD-SA-v2.0
/// §4.1). Papers whose extraction yields too little text — typically scanned
/// or image-only documents — are flagged so the user is warned before they
/// join a run rather than silently degrading a briefing.
/// </summary>
public class PaperTextExtractor
{
    private const int MaxChars = 20_000;      // aligned with web-source truncation
    private const int MinUsefulChars = 500;   // below this the paper is flagged

    public (string Text, bool Ok) Extract(byte[] pdfBytes)
    {
        try
        {
            var sb = new StringBuilder();
            using var document = PdfDocument.Open(pdfBytes);
            foreach (var page in document.GetPages())
            {
                sb.AppendLine(page.Text);
                if (sb.Length > MaxChars) break;
            }
            var text = sb.ToString().Trim();
            if (text.Length > MaxChars) text = text[..MaxChars];
            return (text, text.Length >= MinUsefulChars);
        }
        catch
        {
            return ("", false);
        }
    }
}

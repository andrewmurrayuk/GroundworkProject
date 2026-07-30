using AngleSharp;
using AngleSharp.Dom;
using System.Text.RegularExpressions;

namespace Groundwork.Services;

/// <summary>
/// Fetches a URL and reduces it to clean readable text for LLM extraction.
/// </summary>
public class ContentFetcher
{
    private readonly HttpClient _http;
    private const int MaxChars = 20_000;

    public ContentFetcher(HttpClient http)
    {
        _http = http;
        _http.Timeout = TimeSpan.FromSeconds(30);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (compatible; GroundworkResearch/1.0)");
    }

    public async Task<(string Title, string Text)> FetchAsync(string url)
    {
        var html = await _http.GetStringAsync(url);

        var context = BrowsingContext.New(Configuration.Default);
        var doc = await context.OpenAsync(req => req.Content(html).Address(url));

        var title = doc.Title?.Trim() ?? url;

        foreach (var el in doc.QuerySelectorAll(
                     "script, style, nav, footer, header, noscript, svg, form, iframe, aside")
                     .ToList())
        {
            el.Remove();
        }

        var text = doc.Body?.TextContent ?? "";
        text = Regex.Replace(text, @"[ \t]+", " ");
        text = Regex.Replace(text, @"\n\s*\n+", "\n\n").Trim();

        if (text.Length > MaxChars) text = text[..MaxChars];
        return (title, text);
    }
}

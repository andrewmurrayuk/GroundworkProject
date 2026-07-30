using System.Text.Json;
using Groundwork.Models;

namespace Groundwork.Services;

/// <summary>
/// LLM-driven source discovery. Sends the research brief and seed topics to
/// Claude with live web search enabled, and gets back a vetted list of
/// candidate URLs with a rationale for each.
/// </summary>
public class DiscoveryService
{
    private readonly ClaudeClient _claude;
    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNameCaseInsensitive = true };

    public DiscoveryService(ClaudeClient claude) => _claude = claude;

    public async Task<List<SourceItem>> DiscoverAsync(
        string brief, string topics, int maxSources = 12)
    {
        const string system =
            "You are a desk-research assistant. You search the live web to find " +
            "high-quality, relevant sources for a research brief. You favour " +
            "primary sources: organisation websites, published reports, government " +
            "pages, practitioner blogs, and technical write-ups. You avoid thin " +
            "aggregator pages and paywalled content where possible. " +
            "Your final reply must be ONLY a JSON array, no prose, no markdown fences.";

        var user =
            $"Research brief:\n{brief}\n\n" +
            $"Seed topics / queries to explore:\n{topics}\n\n" +
            $"Search the web and identify up to {maxSources} of the most useful " +
            "source URLs for this brief. For each, reply with an object: " +
            "{\"url\": \"...\", \"title\": \"...\", \"rationale\": \"one sentence on why this source matters\"}. " +
            "Reply with ONLY the JSON array.";

        var raw = await _claude.MessageAsync(system, user, useWebSearch: true, maxTokens: 4000);
        var json = ClaudeClient.StripFences(raw);

        var items = new List<SourceItem>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                var url = el.TryGetProperty("url", out var u) ? u.GetString() : null;
                if (string.IsNullOrWhiteSpace(url)) continue;
                items.Add(new SourceItem
                {
                    Url = url!,
                    Title = el.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "",
                    Rationale = el.TryGetProperty("rationale", out var r) ? r.GetString() ?? "" : "",
                    Origin = "discovery",
                    Stage = SourceStage.Discovered
                });
            }
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Discovery response was not valid JSON: {ex.Message}");
        }
        return items;
    }
}

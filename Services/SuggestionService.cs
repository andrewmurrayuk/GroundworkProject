using System.Text.Json;

namespace Groundwork.Services;

/// <summary>
/// Drafts a research brief and seed topics from a short topic statement.
/// A single fast Claude call with no web search - the output lands in the
/// editable input fields for the user to review before running the pipeline.
/// </summary>
public class SuggestionService
{
    private readonly ClaudeClient _claude;
    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNameCaseInsensitive = true };

    public SuggestionService(ClaudeClient claude) => _claude = claude;

    public async Task<(string Brief, List<string> Topics)> SuggestAsync(string topic)
    {
        const string system =
            "You draft desk-research inputs for a research pipeline. Given a short " +
            "topic, you write (1) a research brief and (2) seed search topics. " +
            "A good brief names the four signal areas the pipeline extracts: which " +
            "organisations are active in the space; what frustrations, barriers and " +
            "gaps practitioners describe in their own words; what tools, platforms " +
            "and technical solutions are in use or being built, and by whom; and " +
            "what datasets exist - content, scale or volume, holders, and access. " +
            "Good seed topics are 3-8 word search queries that mix organisation " +
            "names, data sources, and 'frustrations/barriers' phrasing so discovery " +
            "surfaces practitioner voices as well as official publications. Where " +
            "the topic implies a country context, reflect it; do not invent one. " +
            "Your reply must be ONLY a JSON object, no prose, no markdown fences.";

        var user =
            $"Topic: {topic}\n\n" +
            "Reply with ONLY this JSON shape:\n" +
            "{\n" +
            "  \"brief\": \"a research brief of roughly 120-180 words covering the four signal areas\",\n" +
            "  \"topics\": [\"10-12 seed search topics, one per array item\"]\n" +
            "}";

        var raw = await _claude.MessageAsync(system, user, useWebSearch: false, maxTokens: 1500);
        var json = ClaudeClient.StripFences(raw);

        using var doc = JsonDocument.Parse(json);
        var brief = doc.RootElement.TryGetProperty("brief", out var b)
            ? b.GetString() ?? "" : "";
        var topics = new List<string>();
        if (doc.RootElement.TryGetProperty("topics", out var t))
            foreach (var el in t.EnumerateArray())
                if (el.GetString() is { Length: > 0 } s) topics.Add(s);

        return (brief, topics);
    }
}

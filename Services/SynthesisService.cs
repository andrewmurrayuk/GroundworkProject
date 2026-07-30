using System.Text;
using System.Text.Json;
using Groundwork.Models;

namespace Groundwork.Services;

/// <summary>
/// Cross-source synthesis: clusters every per-document extract into a single
/// cited report structure ready for Word export.
/// </summary>
public class SynthesisService
{
    private readonly ClaudeClient _claude;
    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNameCaseInsensitive = true };

    public SynthesisService(ClaudeClient claude) => _claude = claude;

    public async Task<ReportModel> SynthesiseAsync(string brief, List<SourceItem> sources)
    {
        const string system =
            "You are a research analyst producing a desk-research briefing from " +
            "structured extracts of multiple web sources. Every claim you make must " +
            "trace back to at least one source URL you were given. Cluster signals " +
            "into themes; do not simply list sources one by one. Be selective: " +
            "quality over completeness. " +
            "Your reply must be ONLY a JSON object, no prose, no markdown fences. " +
            "It must be COMPLETE, valid JSON - never stop mid-structure.";

        var sb = new StringBuilder();
        sb.AppendLine($"Research brief:\n{brief}\n");
        sb.AppendLine("Per-source extracts:");
        foreach (var s in sources.Where(s => s.Extract is not null))
        {
            sb.AppendLine($"--- SOURCE: {s.Url}");
            sb.AppendLine($"Title: {s.Title}");
            sb.AppendLine(JsonSerializer.Serialize(s.Extract));
        }

        sb.AppendLine();
        sb.AppendLine("Produce the briefing. Reply with ONLY this JSON shape:");
        sb.AppendLine("""
{
  "title": "short report title",
  "executiveSummary": "3-5 sentences a busy reader can act on",
  "themes": [{"heading": "...", "narrative": "2-4 sentences", "sourceUrls": ["..."]}],
  "organisations": [{"name": "...", "whatTheyDo": "...", "sourceUrls": ["..."]}],
  "toolsAndTech": [{"name": "...", "context": "how/why it is used", "sourceUrls": ["..."]}],
  "datasets": [{"name": "...", "details": "content, scale, access", "sourceUrls": ["..."]}],
  "gapsAndOpportunities": ["..."],
  "suggestedNextSteps": ["..."]
}
""");

        sb.AppendLine("Limits: at most 6 themes, 12 organisations, 10 tools, " +
            "10 datasets, 8 gapsAndOpportunities, 6 suggestedNextSteps. " +
            "Prioritise the strongest, best-evidenced items within those limits.");

        var raw = await _claude.MessageAsync(system, sb.ToString(), useWebSearch: false, maxTokens: 16000);
        var json = ClaudeClient.StripFences(raw);
        return JsonSerializer.Deserialize<ReportModel>(json, JsonOpts)
               ?? new ReportModel { Title = "Synthesis failed", ExecutiveSummary = "No report data returned." };
    }
}

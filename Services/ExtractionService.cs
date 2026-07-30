using System.Text.Json;
using Groundwork.Models;

namespace Groundwork.Services;

/// <summary>
/// Per-document structured extraction: turns a page's clean text into
/// organisations, pain points, tools and datasets as JSON.
/// </summary>
public class ExtractionService
{
    private readonly ClaudeClient _claude;
    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNameCaseInsensitive = true };

    public ExtractionService(ClaudeClient claude) => _claude = claude;

    public async Task<DocExtract> ExtractAsync(string brief, string url, string text)
    {
        const string system =
            "You extract structured research signals from a single web page. " +
            "You only report what the text actually supports - no invention, no padding. " +
            "Your reply must be ONLY a JSON object, no prose, no markdown fences.";

        var user =
            $"Research brief:\n{brief}\n\n" +
            $"Source URL: {url}\n\n" +
            "Page text:\n\"\"\"\n" + text + "\n\"\"\"\n\n" +
            "Extract and reply with ONLY this JSON shape:\n" +
            "{\n" +
            "  \"organisations\": [{\"name\": \"...\", \"role\": \"what they do in this space\"}],\n" +
            "  \"painPoints\": [\"frustrations or gaps stated or clearly implied\"],\n" +
            "  \"tools\": [\"tools, technologies or platforms mentioned\"],\n" +
            "  \"datasets\": [{\"name\": \"...\", \"scaleOrVolume\": \"scale/volume if stated, else empty\"}],\n" +
            "  \"summary\": \"2-3 sentence summary of what this source contributes to the brief\",\n" +
            "  \"relevance\": 1\n" +
            "}\n" +
            "relevance is 1-5 where 5 means highly relevant to the brief. " +
            "Use empty arrays where the page offers nothing for a field.";

        var raw = await _claude.MessageAsync(system, user, useWebSearch: false, maxTokens: 2000);
        var json = ClaudeClient.StripFences(raw);
        return JsonSerializer.Deserialize<DocExtract>(json, JsonOpts)
               ?? new DocExtract { Summary = "Extraction returned no data." };
    }
}

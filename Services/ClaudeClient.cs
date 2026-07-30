using System.Text;
using System.Text.Json;

namespace Groundwork.Services;

public class ClaudeClient
{
    private readonly HttpClient _http;
    private readonly string _model;
    private readonly string _baseUrl;

    public ClaudeClient(HttpClient http, IConfiguration config)
    {
        _http = http;
        _model = config["Anthropic:Model"] ?? "claude-sonnet-4-6";
        _baseUrl = config["Anthropic:BaseUrl"] ?? "https://api.anthropic.com/v1/messages";

        var apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")
                     ?? config["Anthropic:ApiKey"]
                     ?? throw new InvalidOperationException(
                         "ANTHROPIC_API_KEY environment variable is not set.");

        _http.DefaultRequestHeaders.Add("x-api-key", apiKey);
        _http.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
        _http.Timeout = TimeSpan.FromMinutes(5);
    }

    /// <summary>
    /// Sends a message to Claude. When useWebSearch is true, the server-side
    /// web search tool is enabled so Claude can search the live web.
    /// Returns the concatenated text of all text blocks in the response.
    /// </summary>
    public async Task<string> MessageAsync(
        string system, string user, bool useWebSearch = false, int maxTokens = 4000)
    {
        var body = new Dictionary<string, object?>
        {
            ["model"] = _model,
            ["max_tokens"] = maxTokens,
            ["system"] = system,
            ["messages"] = new[]
            {
                new { role = "user", content = user }
            }
        };

        if (useWebSearch)
        {
            body["tools"] = new object[]
            {
                new Dictionary<string, object>
                {
                    ["type"] = "web_search_20250305",
                    ["name"] = "web_search",
                    ["max_uses"] = 8
                }
            };
        }

        var json = JsonSerializer.Serialize(body);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync(_baseUrl, content);

        var responseText = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Claude API error {(int)response.StatusCode}: {Truncate(responseText, 500)}");

        using var doc = JsonDocument.Parse(responseText);
        var sb = new StringBuilder();
        foreach (var block in doc.RootElement.GetProperty("content").EnumerateArray())
        {
            if (block.TryGetProperty("type", out var t) && t.GetString() == "text"
                && block.TryGetProperty("text", out var txt))
            {
                sb.Append(txt.GetString());
            }
        }
        return sb.ToString();
    }

    /// <summary>Strips markdown code fences so JSON responses parse cleanly.</summary>
    public static string StripFences(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.StartsWith("```"))
        {
            var firstNewline = trimmed.IndexOf('\n');
            if (firstNewline >= 0) trimmed = trimmed[(firstNewline + 1)..];
            var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (lastFence >= 0) trimmed = trimmed[..lastFence];
        }
        // If Claude added prose before the JSON, cut to the first brace/bracket.
        var firstBrace = trimmed.IndexOfAny(new[] { '{', '[' });
        if (firstBrace > 0) trimmed = trimmed[firstBrace..];
        return trimmed.Trim();
    }

    private static string Truncate(string s, int len) =>
        s.Length <= len ? s : s[..len] + "…";
}

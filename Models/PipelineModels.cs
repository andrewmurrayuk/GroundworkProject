using System.Text.Json.Serialization;

namespace Groundwork.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SourceStage
{
    Discovered,
    Fetching,
    Fetched,
    Extracting,
    Extracted,
    Failed
}

public class SourceItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Url { get; set; } = "";
    public string Title { get; set; } = "";
    public string Origin { get; set; } = "discovery"; // "discovery" | "manual"
    public string Rationale { get; set; } = "";
    public SourceStage Stage { get; set; } = SourceStage.Discovered;
    public string? Error { get; set; }
    [JsonIgnore]
    public string? CleanText { get; set; }
    public DocExtract? Extract { get; set; }
}

public class DocExtract
{
    public List<OrgMention> Organisations { get; set; } = new();
    public List<string> PainPoints { get; set; } = new();
    public List<string> Tools { get; set; } = new();
    public List<DatasetMention> Datasets { get; set; } = new();
    public string Summary { get; set; } = "";
    public int Relevance { get; set; } = 3;
}

public class OrgMention
{
    public string Name { get; set; } = "";
    public string Role { get; set; } = "";
}

public class DatasetMention
{
    public string Name { get; set; } = "";
    public string ScaleOrVolume { get; set; } = "";
}

public class ReportModel
{
    public string Title { get; set; } = "";
    public string ExecutiveSummary { get; set; } = "";
    public List<ReportTheme> Themes { get; set; } = new();
    public List<ReportOrg> Organisations { get; set; } = new();
    public List<ReportTool> ToolsAndTech { get; set; } = new();
    public List<ReportDataset> Datasets { get; set; } = new();
    public List<string> GapsAndOpportunities { get; set; } = new();
    public List<string> SuggestedNextSteps { get; set; } = new();
}

public class ReportTheme
{
    public string Heading { get; set; } = "";
    public string Narrative { get; set; } = "";
    public List<string> SourceUrls { get; set; } = new();
}

public class ReportOrg
{
    public string Name { get; set; } = "";
    public string WhatTheyDo { get; set; } = "";
    public List<string> SourceUrls { get; set; } = new();
}

public class ReportTool
{
    public string Name { get; set; } = "";
    public string Context { get; set; } = "";
    public List<string> SourceUrls { get; set; } = new();
}

public class ReportDataset
{
    public string Name { get; set; } = "";
    public string Details { get; set; } = "";
    public List<string> SourceUrls { get; set; } = new();
}

public record RunRequest(string Brief, string Topics, string Urls, List<Guid>? PaperIds);

public record SuggestRequest(string Topic);

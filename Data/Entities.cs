namespace Groundwork.Data;

/// <summary>
/// The v2.0 persistence schema (GW-HLD-SA-v2.0 §5.1). The full aggregate is
/// defined in phase 2.0-A even though project CRUD arrives in 2.0-B, so the
/// schema is stable across phases. Per P10 every artefact belongs to a project.
/// </summary>
public class Project
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public DateTime CreatedUtc { get; set; }
    public DateTime? ArchivedUtc { get; set; }

    public List<Brief> Briefs { get; set; } = new();
    public List<SeedList> SeedLists { get; set; } = new();
    public List<Paper> Papers { get; set; } = new();
    public List<Run> Runs { get; set; } = new();
}

public class Brief
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public string Label { get; set; } = "";
    public string Text { get; set; } = "";
    public DateTime CreatedUtc { get; set; }
}

public class SeedList
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public string Label { get; set; } = "";
    /// <summary>Seed topics, newline-separated, as entered.</summary>
    public string TopicsText { get; set; } = "";
    public DateTime CreatedUtc { get; set; }
}

public class Paper
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public string FileName { get; set; } = "";
    public byte[] PdfBytes { get; set; } = Array.Empty<byte>();
    public string ExtractedText { get; set; } = "";
    /// <summary>False when extraction yielded too little text to be useful.</summary>
    public bool ExtractionOk { get; set; }
    public DateTime UploadedUtc { get; set; }
}

public class Run
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    /// <summary>Snapshot of the brief used, per §5.2 — past reports stay explicable.</summary>
    public string BriefSnapshot { get; set; } = "";
    public string SeedsSnapshot { get; set; } = "";
    public string Status { get; set; } = "queued";
    public string? FailureMessage { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime? CompletedUtc { get; set; }

    public List<RunSource> Sources { get; set; } = new();
    public Report? Report { get; set; }
}

public class RunSource
{
    public Guid Id { get; set; }
    public Guid RunId { get; set; }
    /// <summary>Correlates with the in-flight SourceItem id used in SignalR events.</summary>
    public string ClientId { get; set; } = "";
    public string Url { get; set; } = "";
    public string Title { get; set; } = "";
    public string Origin { get; set; } = "";
    public string Rationale { get; set; } = "";
    public string Stage { get; set; } = "";
    public string? Error { get; set; }
    public string? ExtractJson { get; set; }
}

public class Report
{
    public Guid Id { get; set; }
    public Guid RunId { get; set; }
    public string BriefingJson { get; set; } = "";
    public byte[] DocxBytes { get; set; } = Array.Empty<byte>();
    public DateTime CreatedUtc { get; set; }
}

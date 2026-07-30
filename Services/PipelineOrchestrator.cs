using Groundwork.Hubs;
using Groundwork.Models;
using Microsoft.AspNetCore.SignalR;

namespace Groundwork.Services;

/// <summary>
/// Runs the full pipeline for a run and broadcasts progress to the browser
/// over SignalR: discovery -> fetch -> per-doc extraction -> synthesis -> docx.
/// v2.0-A: every stage boundary is persisted through RunService, so run
/// history survives restarts (GW-HLD-SA-v2.0 §7.1).
/// </summary>
public class PipelineOrchestrator
{
    private readonly DiscoveryService _discovery;
    private readonly ContentFetcher _fetcher;
    private readonly ExtractionService _extraction;
    private readonly SynthesisService _synthesis;
    private readonly ReportBuilder _reportBuilder;
    private readonly RunService _runs;
    private readonly IHubContext<PipelineHub> _hub;
    private readonly ILogger<PipelineOrchestrator> _log;

    public PipelineOrchestrator(
        DiscoveryService discovery,
        ContentFetcher fetcher,
        ExtractionService extraction,
        SynthesisService synthesis,
        ReportBuilder reportBuilder,
        RunService runs,
        IHubContext<PipelineHub> hub,
        ILogger<PipelineOrchestrator> log)
    {
        _discovery = discovery;
        _fetcher = fetcher;
        _extraction = extraction;
        _synthesis = synthesis;
        _reportBuilder = reportBuilder;
        _runs = runs;
        _hub = hub;
        _log = log;
    }

    public async Task RunAsync(Guid runId, string brief, string topics, List<string> manualUrls, List<SourceItem> paperSources)
    {
        var groupId = runId.ToString("N");
        var group = _hub.Clients.Group(groupId);
        var sources = new List<SourceItem>();
        try
        {
            // 1. Discovery (LLM + live web search)
            await _runs.SetStatusAsync(runId, "discovering");
            await group.SendAsync("JobStatus", "Searching the web for sources…");

            List<SourceItem> discovered = new();
            if (!string.IsNullOrWhiteSpace(topics))
            {
                try
                {
                    discovered = await _discovery.DiscoverAsync(brief, topics);
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Discovery failed for run {RunId}", runId);
                    await group.SendAsync("JobStatus",
                        "Source discovery hit a problem — continuing with any URLs you provided.");
                }
            }

            // 2. Merge manual URLs (deduplicated)
            foreach (var url in manualUrls)
            {
                if (discovered.Any(s => s.Url.Equals(url, StringComparison.OrdinalIgnoreCase)))
                    continue;
                discovered.Add(new SourceItem
                {
                    Url = url, Title = url, Origin = "manual",
                    Rationale = "Provided directly.", Stage = SourceStage.Discovered
                });
            }

            sources.AddRange(discovered);
            sources.AddRange(paperSources); // third entry point (P2): pre-fetched
            await _runs.AddSourcesAsync(runId, sources);
            foreach (var s in sources)
                await group.SendAsync("SourceAdded", s);

            if (sources.Count == 0)
            {
                await _runs.SetStatusAsync(runId, "failed", "No sources found.");
                await group.SendAsync("JobFailed",
                    "No sources found. Add seed topics or paste URLs and run again.");
                return;
            }

            // 3. Fetch + extract, limited parallelism to stay polite
            await _runs.SetStatusAsync(runId, "extracting");
            await group.SendAsync("JobStatus", $"Reading {sources.Count} sources…");

            using var gate = new SemaphoreSlim(3);
            var tasks = sources.Select(async source =>
            {
                await gate.WaitAsync();
                try
                {
                    if (source.Origin == "paper")
                    {
                        // Papers enter at Fetched: text was extracted at upload (§5.2).
                        source.Stage = SourceStage.Fetched;
                        await group.SendAsync("SourceUpdated", source);
                        await _runs.UpdateSourceAsync(runId, source);
                    }
                    else
                    {
                        source.Stage = SourceStage.Fetching;
                        await group.SendAsync("SourceUpdated", source);
                        await _runs.UpdateSourceAsync(runId, source);

                        var (title, text) = await _fetcher.FetchAsync(source.Url);
                        if (string.IsNullOrWhiteSpace(source.Title) || source.Origin == "manual")
                            source.Title = title;
                        source.CleanText = text;
                        source.Stage = SourceStage.Fetched;
                        await group.SendAsync("SourceUpdated", source);
                        await _runs.UpdateSourceAsync(runId, source);
                    }

                    if (string.IsNullOrWhiteSpace(source.CleanText))
                        throw new InvalidOperationException("The source contained no readable text.");

                    source.Stage = SourceStage.Extracting;
                    await group.SendAsync("SourceUpdated", source);
                    await _runs.UpdateSourceAsync(runId, source);

                    source.Extract = await _extraction.ExtractAsync(brief, source.Url, source.CleanText!);
                    source.Stage = SourceStage.Extracted;
                    await group.SendAsync("SourceUpdated", source);
                    await _runs.UpdateSourceAsync(runId, source);
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Source failed: {Url}", source.Url);
                    source.Stage = SourceStage.Failed;
                    source.Error = ex.Message;
                    await group.SendAsync("SourceUpdated", source);
                    await _runs.UpdateSourceAsync(runId, source);
                }
                finally
                {
                    gate.Release();
                }
            });
            await Task.WhenAll(tasks);

            var usable = sources.Where(s => s.Stage == SourceStage.Extracted).ToList();
            if (usable.Count == 0)
            {
                await _runs.SetStatusAsync(runId, "failed", "No sources could be read.");
                await group.SendAsync("JobFailed",
                    "None of the sources could be read. Check the URLs and try again.");
                return;
            }

            // 4. Synthesis
            await _runs.SetStatusAsync(runId, "synthesising");
            await group.SendAsync("JobStatus",
                $"Synthesising a briefing from {usable.Count} sources…");
            var report = await _synthesis.SynthesiseAsync(brief, usable);

            // 5. Word document, persisted with the run
            await group.SendAsync("JobStatus", "Building the Word document…");
            var docx = _reportBuilder.Build(report, sources);
            await _runs.SaveReportAsync(runId, report, docx);
            await _runs.SetStatusAsync(runId, "complete");

            await group.SendAsync("ReportReady", new
            {
                jobId = groupId,
                report,
                downloadUrl = $"/api/report/{runId}"
            });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Pipeline failed for run {RunId}", runId);
            await _runs.SetStatusAsync(runId, "failed", ex.Message);
            await group.SendAsync("JobFailed", $"The pipeline stopped: {ex.Message}");
        }
    }
}

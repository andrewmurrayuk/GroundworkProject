using System.Text.Json;
using Groundwork.Data;
using Groundwork.Models;
using Microsoft.EntityFrameworkCore;

namespace Groundwork.Services;

/// <summary>
/// The only writer of run state (GW-HLD-SA-v2.0 §4.3). Creates a run before the
/// pipeline starts, records every stage boundary, and persists the briefing and
/// document — so run history is accurate across restarts.
/// Uses the context factory so it can be a singleton alongside the orchestrator.
/// </summary>
public class RunService
{
    private readonly IDbContextFactory<GroundworkDbContext> _dbFactory;

    public RunService(IDbContextFactory<GroundworkDbContext> dbFactory)
        => _dbFactory = dbFactory;

    public async Task<Run> CreateRunAsync(Guid projectId, string brief, string seeds)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var run = new Run
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            BriefSnapshot = brief,
            SeedsSnapshot = seeds,
            Status = "queued",
            CreatedUtc = DateTime.UtcNow
        };
        db.Runs.Add(run);
        await db.SaveChangesAsync();
        return run;
    }

    public async Task SetStatusAsync(Guid runId, string status, string? failure = null)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var run = await db.Runs.FindAsync(runId);
        if (run is null) return;
        run.Status = status;
        run.FailureMessage = failure;
        if (status is "complete" or "failed" or "interrupted")
            run.CompletedUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    public async Task AddSourcesAsync(Guid runId, IEnumerable<SourceItem> sources)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        foreach (var s in sources)
        {
            db.RunSources.Add(new RunSource
            {
                Id = Guid.NewGuid(),
                RunId = runId,
                ClientId = s.Id,
                Url = s.Url,
                Title = s.Title,
                Origin = s.Origin,
                Rationale = s.Rationale,
                Stage = s.Stage.ToString()
            });
        }
        await db.SaveChangesAsync();
    }

    public async Task UpdateSourceAsync(Guid runId, SourceItem s)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var row = await db.RunSources
            .FirstOrDefaultAsync(x => x.RunId == runId && x.ClientId == s.Id);
        if (row is null) return;
        row.Title = s.Title;
        row.Stage = s.Stage.ToString();
        row.Error = s.Error;
        if (s.Extract is not null)
            row.ExtractJson = JsonSerializer.Serialize(s.Extract);
        await db.SaveChangesAsync();
    }

    public async Task SaveReportAsync(Guid runId, ReportModel briefing, byte[] docx)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        db.Reports.Add(new Report
        {
            Id = Guid.NewGuid(),
            RunId = runId,
            BriefingJson = JsonSerializer.Serialize(briefing),
            DocxBytes = docx,
            CreatedUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    public async Task<byte[]?> GetReportBytesAsync(Guid runId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Reports.Where(r => r.RunId == runId)
            .Select(r => r.DocxBytes).FirstOrDefaultAsync();
    }

    /// <summary>Startup sweep: any run left non-terminal by a restart is marked interrupted.</summary>
    public async Task MarkStaleRunsInterruptedAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var stale = await db.Runs
            .Where(r => r.Status != "complete" && r.Status != "failed" && r.Status != "interrupted")
            .ToListAsync();
        foreach (var r in stale)
        {
            r.Status = "interrupted";
            r.FailureMessage = "The application restarted while this run was in progress.";
            r.CompletedUtc = DateTime.UtcNow;
        }
        await db.SaveChangesAsync();
    }
}

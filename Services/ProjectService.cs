using Groundwork.Data;
using Microsoft.EntityFrameworkCore;

namespace Groundwork.Services;

/// <summary>
/// Project lifecycle and content management (GW-HLD-SA-v2.0 §4.1). Enforces
/// P10: every operation is scoped to a project. Briefs and seed lists are
/// versioned — saving creates a new version; "current" is the latest.
/// </summary>
public class ProjectService
{
    private readonly IDbContextFactory<GroundworkDbContext> _dbFactory;
    private readonly PaperTextExtractor _extractor;

    public ProjectService(IDbContextFactory<GroundworkDbContext> dbFactory,
        PaperTextExtractor extractor)
    {
        _dbFactory = dbFactory;
        _extractor = extractor;
    }

    public async Task<List<object>> ListAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Projects
            .Where(p => p.ArchivedUtc == null)
            .OrderByDescending(p => p.CreatedUtc)
            .Select(p => (object)new
            {
                id = p.Id,
                name = p.Name,
                description = p.Description,
                createdUtc = p.CreatedUtc,
                runCount = p.Runs.Count,
                lastRunUtc = p.Runs.OrderByDescending(r => r.CreatedUtc)
                    .Select(r => (DateTime?)r.CreatedUtc).FirstOrDefault()
            })
            .ToListAsync();
    }

    public async Task<Project> CreateAsync(string name, string description)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            Description = description.Trim(),
            CreatedUtc = DateTime.UtcNow
        };
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        return project;
    }

    public async Task<object?> GetAsync(Guid id)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var project = await db.Projects
            .FirstOrDefaultAsync(p => p.Id == id && p.ArchivedUtc == null);
        if (project is null) return null;

        var brief = await db.Briefs.Where(b => b.ProjectId == id)
            .OrderByDescending(b => b.CreatedUtc).FirstOrDefaultAsync();
        var seeds = await db.SeedLists.Where(s => s.ProjectId == id)
            .OrderByDescending(s => s.CreatedUtc).FirstOrDefaultAsync();

        return new
        {
            id = project.Id,
            name = project.Name,
            description = project.Description,
            brief = brief?.Text ?? "",
            seeds = seeds?.TopicsText ?? ""
        };
    }

    public async Task<bool> UpdateAsync(Guid id, string? name, string? description)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var project = await db.Projects.FindAsync(id);
        if (project is null || project.ArchivedUtc is not null) return false;
        if (!string.IsNullOrWhiteSpace(name)) project.Name = name.Trim();
        if (description is not null) project.Description = description.Trim();
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> ArchiveAsync(Guid id)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var project = await db.Projects.FindAsync(id);
        if (project is null) return false;
        project.ArchivedUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> ExistsAsync(Guid id)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Projects.AnyAsync(p => p.Id == id && p.ArchivedUtc == null);
    }

    /// <summary>Saves a new brief version only when the text differs from the current one.</summary>
    public async Task SaveBriefIfChangedAsync(Guid projectId, string text)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var current = await db.Briefs.Where(b => b.ProjectId == projectId)
            .OrderByDescending(b => b.CreatedUtc).FirstOrDefaultAsync();
        if (current?.Text == text) return;
        db.Briefs.Add(new Brief
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            Label = $"v{DateTime.UtcNow:yyyyMMdd-HHmm}",
            Text = text,
            CreatedUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    /// <summary>Saves a new seed-list version only when the topics differ from the current one.</summary>
    public async Task SaveSeedsIfChangedAsync(Guid projectId, string topicsText)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var current = await db.SeedLists.Where(s => s.ProjectId == projectId)
            .OrderByDescending(s => s.CreatedUtc).FirstOrDefaultAsync();
        if (current?.TopicsText == topicsText) return;
        db.SeedLists.Add(new SeedList
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            Label = $"v{DateTime.UtcNow:yyyyMMdd-HHmm}",
            TopicsText = topicsText,
            CreatedUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    // ---- papers (2.0-C) ----

    public async Task<object> AddPaperAsync(Guid projectId, string fileName, byte[] pdfBytes)
    {
        var (text, ok) = _extractor.Extract(pdfBytes);
        await using var db = await _dbFactory.CreateDbContextAsync();
        var paper = new Paper
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            FileName = fileName,
            PdfBytes = pdfBytes,
            ExtractedText = text,
            ExtractionOk = ok,
            UploadedUtc = DateTime.UtcNow
        };
        db.Papers.Add(paper);
        await db.SaveChangesAsync();
        return new { id = paper.Id, fileName = paper.FileName, extractionOk = ok,
            uploadedUtc = paper.UploadedUtc };
    }

    public async Task<List<object>> ListPapersAsync(Guid projectId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Papers
            .Where(p => p.ProjectId == projectId)
            .OrderByDescending(p => p.UploadedUtc)
            .Select(p => (object)new
            {
                id = p.Id,
                fileName = p.FileName,
                extractionOk = p.ExtractionOk,
                uploadedUtc = p.UploadedUtc
            })
            .ToListAsync();
    }

    public async Task<bool> DeletePaperAsync(Guid projectId, Guid paperId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var paper = await db.Papers
            .FirstOrDefaultAsync(p => p.Id == paperId && p.ProjectId == projectId);
        if (paper is null) return false;
        db.Papers.Remove(paper);
        await db.SaveChangesAsync();
        return true;
    }

    /// <summary>Loads selected papers (validated against the project) as pre-fetched pipeline sources.</summary>
    public async Task<List<Models.SourceItem>> GetPaperSourcesAsync(Guid projectId, List<Guid> paperIds)
    {
        if (paperIds.Count == 0) return new();
        await using var db = await _dbFactory.CreateDbContextAsync();
        var papers = await db.Papers
            .Where(p => p.ProjectId == projectId && paperIds.Contains(p.Id))
            .Select(p => new { p.Id, p.FileName, p.ExtractedText })
            .ToListAsync();
        return papers.Select(p => new Models.SourceItem
        {
            Url = $"paper:{p.Id}",
            Title = p.FileName,
            Origin = "paper",
            Rationale = "Uploaded paper.",
            CleanText = p.ExtractedText,
            Stage = Models.SourceStage.Discovered
        }).ToList();
    }

    public async Task<List<object>> ListRunsAsync(Guid projectId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Runs
            .Where(r => r.ProjectId == projectId)
            .OrderByDescending(r => r.CreatedUtc)
            .Select(r => (object)new
            {
                id = r.Id,
                status = r.Status,
                createdUtc = r.CreatedUtc,
                completedUtc = r.CompletedUtc,
                failureMessage = r.FailureMessage,
                sourceCount = r.Sources.Count,
                hasReport = r.Report != null
            })
            .ToListAsync();
    }
}

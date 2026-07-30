using Groundwork.Data;
using Groundwork.Hubs;
using Groundwork.Models;
using Groundwork.Services;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ---- access gate (2.0-D, GW-HLD-SA-v2.0 §9.1) ----
// P11: an unauthenticated deployment of this version is a defective deployment,
// so a missing passphrase fails startup rather than silently opening the door.
var accessKey = Environment.GetEnvironmentVariable("GROUNDWORK_ACCESS_KEY")
    ?? throw new InvalidOperationException(
        "GROUNDWORK_ACCESS_KEY is not set. The access gate is mandatory from v2.0-D (P11).");

builder.Services.AddRazorPages();

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/login";
        o.ExpireTimeSpan = TimeSpan.FromDays(30);
        o.SlidingExpiration = true;
        o.Cookie.Name = "gw.auth";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Lax;
        // APIs and the SignalR hub get a clean 401; pages get the login redirect.
        o.Events.OnRedirectToLogin = ctx =>
        {
            if (ctx.Request.Path.StartsWithSegments("/api") ||
                ctx.Request.Path.StartsWithSegments("/hubs"))
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            else
                ctx.Response.Redirect(ctx.RedirectUri);
            return Task.CompletedTask;
        };
    });

// Everything requires authentication unless explicitly AllowAnonymous.
builder.Services.AddAuthorization(o =>
    o.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build());
builder.Services.AddSignalR();
builder.Services.Configure<FormOptions>(o =>
    o.MultipartBodyLengthLimit = 20 * 1024 * 1024); // §5.3: ~20 MB per paper

// ---- persistence (GW-HLD-SA-v2.0 §10) ----
var connectionString = BuildConnectionString(builder.Configuration);
builder.Services.AddDbContextFactory<GroundworkDbContext>(o => o.UseNpgsql(connectionString));

builder.Services.AddHttpClient<ClaudeClient>();
builder.Services.AddHttpClient<ContentFetcher>();
builder.Services.AddSingleton<DiscoveryService>();
builder.Services.AddSingleton<SuggestionService>();
builder.Services.AddSingleton<ExtractionService>();
builder.Services.AddSingleton<SynthesisService>();
builder.Services.AddSingleton<ReportBuilder>();
builder.Services.AddSingleton<RunService>();
builder.Services.AddSingleton<ProjectService>();
builder.Services.AddSingleton<PaperTextExtractor>();
builder.Services.AddSingleton<PipelineOrchestrator>();

var app = builder.Build();

// Apply migrations before serving traffic (§7.2), mark runs interrupted by the
// restart (§7.1), and ensure the default project exists (P10; project CRUD is 2.0-B).
{
    var dbFactory = app.Services.GetRequiredService<IDbContextFactory<GroundworkDbContext>>();
    await using var db = await dbFactory.CreateDbContextAsync();
    await db.Database.MigrateAsync();

    var project = await db.Projects.OrderBy(p => p.CreatedUtc).FirstOrDefaultAsync();
    if (project is null)
    {
        project = new Project
        {
            Id = Guid.NewGuid(),
            Name = "Default Project",
            Description = "Created automatically in phase 2.0-A; project management arrives in 2.0-B.",
            CreatedUtc = DateTime.UtcNow
        };
        db.Projects.Add(project);
        await db.SaveChangesAsync();
    }

    await app.Services.GetRequiredService<RunService>().MarkStaleRunsInterruptedAsync();
}

app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapRazorPages();
app.MapHub<PipelineHub>("/hubs/pipeline");

// Validate the shared passphrase and issue the auth cookie (§6.1).
app.MapPost("/api/auth", async (AuthRequest req, HttpContext http) =>
{
    var supplied = Encoding.UTF8.GetBytes(req.Key ?? "");
    var expected = Encoding.UTF8.GetBytes(accessKey);
    var ok = supplied.Length == expected.Length &&
             CryptographicOperations.FixedTimeEquals(supplied, expected);
    if (!ok) return Results.Unauthorized();

    var identity = new ClaimsIdentity(
        new[] { new Claim(ClaimTypes.Name, "groundwork-user") },
        CookieAuthenticationDefaults.AuthenticationScheme);
    await http.SignInAsync(
        CookieAuthenticationDefaults.AuthenticationScheme,
        new ClaimsPrincipal(identity),
        new AuthenticationProperties { IsPersistent = true });
    return Results.Ok();
}).AllowAnonymous();

// ---- projects (2.0-B, GW-HLD-SA-v2.0 §6.1) ----

app.MapGet("/api/projects", async (ProjectService projects) =>
    Results.Ok(await projects.ListAsync()));

app.MapPost("/api/projects", async (CreateProjectRequest req, ProjectService projects) =>
{
    if (string.IsNullOrWhiteSpace(req.Name))
        return Results.BadRequest(new { error = "A project name is required." });
    var project = await projects.CreateAsync(req.Name, req.Description ?? "");
    return Results.Ok(new { id = project.Id });
});

app.MapGet("/api/projects/{id:guid}", async (Guid id, ProjectService projects) =>
    await projects.GetAsync(id) is { } detail ? Results.Ok(detail) : Results.NotFound());

app.MapPatch("/api/projects/{id:guid}", async (Guid id, UpdateProjectRequest req, ProjectService projects) =>
    await projects.UpdateAsync(id, req.Name, req.Description) ? Results.Ok() : Results.NotFound());

app.MapDelete("/api/projects/{id:guid}", async (Guid id, ProjectService projects) =>
    await projects.ArchiveAsync(id) ? Results.Ok() : Results.NotFound());

app.MapGet("/api/projects/{id:guid}/runs", async (Guid id, ProjectService projects) =>
    Results.Ok(await projects.ListRunsAsync(id)));

// ---- papers (2.0-C, GW-HLD-SA-v2.0 §6.1) ----

app.MapGet("/api/projects/{id:guid}/papers", async (Guid id, ProjectService projects) =>
    Results.Ok(await projects.ListPapersAsync(id)));

app.MapPost("/api/projects/{id:guid}/papers", async (Guid id, IFormFile file, ProjectService projects) =>
{
    if (!await projects.ExistsAsync(id)) return Results.NotFound();
    if (file.Length == 0) return Results.BadRequest(new { error = "The file is empty." });
    if (file.Length > 20 * 1024 * 1024)
        return Results.BadRequest(new { error = "Papers are capped at 20 MB." });
    if (!file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        return Results.BadRequest(new { error = "Only PDF papers are supported." });

    using var ms = new MemoryStream();
    await file.CopyToAsync(ms);
    var paper = await projects.AddPaperAsync(id, Path.GetFileName(file.FileName), ms.ToArray());
    return Results.Ok(paper);
}).DisableAntiforgery();

app.MapDelete("/api/projects/{id:guid}/papers/{paperId:guid}",
    async (Guid id, Guid paperId, ProjectService projects) =>
        await projects.DeletePaperAsync(id, paperId) ? Results.Ok() : Results.NotFound());

// Draft a research brief and seed topics from a short topic statement (project-scoped).
app.MapPost("/api/projects/{id:guid}/suggest", async (Guid id, SuggestRequest req,
    ProjectService projects, SuggestionService suggestions) =>
{
    if (!await projects.ExistsAsync(id)) return Results.NotFound();
    if (string.IsNullOrWhiteSpace(req.Topic))
        return Results.BadRequest(new { error = "Enter a topic first." });
    try
    {
        var (brief, topics) = await suggestions.SuggestAsync(req.Topic.Trim());
        return Results.Ok(new { brief, topics });
    }
    catch (Exception ex)
    {
        return Results.Problem(title: "Suggestion failed", detail: ex.Message, statusCode: 502);
    }
});

// Start a run within a project. The brief and seeds are saved as new versions
// when changed, then snapshotted onto the run (§5.2).
app.MapPost("/api/projects/{id:guid}/runs", async (Guid id, RunRequest req,
    ProjectService projects, RunService runs, PipelineOrchestrator pipeline) =>
{
    if (!await projects.ExistsAsync(id)) return Results.NotFound();
    if (string.IsNullOrWhiteSpace(req.Brief))
        return Results.BadRequest(new { error = "A research brief is required." });

    var urls = (req.Urls ?? "")
        .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Where(u => Uri.TryCreate(u, UriKind.Absolute, out _))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    var paperSources = await projects.GetPaperSourcesAsync(id, req.PaperIds ?? new List<Guid>());

    if (string.IsNullOrWhiteSpace(req.Topics) && urls.Count == 0 && paperSources.Count == 0)
        return Results.BadRequest(new { error = "Add seed topics, URLs, or select papers." });

    await projects.SaveBriefIfChangedAsync(id, req.Brief.Trim());
    await projects.SaveSeedsIfChangedAsync(id, req.Topics ?? "");

    var run = await runs.CreateRunAsync(id, req.Brief.Trim(), req.Topics ?? "");

    _ = Task.Run(async () =>
    {
        await Task.Delay(1500);
        await pipeline.RunAsync(run.Id, run.BriefSnapshot, run.SeedsSnapshot, urls, paperSources);
    });

    return Results.Ok(new { jobId = run.Id.ToString("N"), runId = run.Id });
});

// Download the persisted Word document — survives restarts and redeploys (2.0-A exit criterion).
app.MapGet("/api/report/{runId:guid}", async (Guid runId, RunService runs) =>
{
    var bytes = await runs.GetReportBytesAsync(runId);
    if (bytes is null || bytes.Length == 0) return Results.NotFound();
    var name = $"groundwork-briefing-{DateTime.UtcNow:yyyyMMdd}.docx";
    return Results.File(bytes,
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document", name);
});

app.Run();

// Render supplies DATABASE_URL as postgres://user:pass@host:port/db; Npgsql
// needs key-value form. Locally, use ConnectionStrings:Groundwork or DATABASE_URL.
static string BuildConnectionString(IConfiguration config)
{
    var url = Environment.GetEnvironmentVariable("DATABASE_URL");
    if (!string.IsNullOrWhiteSpace(url) &&
        (url.StartsWith("postgres://") || url.StartsWith("postgresql://")))
    {
        var uri = new Uri(url);
        var userInfo = uri.UserInfo.Split(':', 2);
        var host = uri.Host;
        var port = uri.Port > 0 ? uri.Port : 5432;
        var database = uri.AbsolutePath.TrimStart('/');
        var user = Uri.UnescapeDataString(userInfo[0]);
        var pass = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : "";
        return $"Host={host};Port={port};Database={database};Username={user};Password={pass};SSL Mode=Require;Trust Server Certificate=true";
    }

    return config.GetConnectionString("Groundwork")
        ?? throw new InvalidOperationException(
            "No database configured. Set DATABASE_URL (postgres://…) or ConnectionStrings:Groundwork.");
}

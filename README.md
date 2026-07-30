# Groundwork

A one-off desk-research pipeline: give it a research brief, it searches the
live web (LLM-driven discovery via the Claude API), fetches and reads each
source, extracts structured signals per document, synthesises a cited
briefing across all sources, and produces a downloadable Word document.

Built as an ASP.NET Core 8 Razor Pages app with SignalR live progress
("evidence ledger") and OpenXML `.docx` generation.

## Pipeline

1. **Discovery** — the brief + seed topics go to Claude with the server-side
   `web_search` tool enabled; it returns candidate URLs with rationales.
2. **Manual URLs** — anything you paste in is merged (deduplicated).
3. **Fetch** — each URL is pulled and reduced to clean text (AngleSharp).
4. **Extraction** — one Claude call per document returns structured JSON:
   organisations, pain points, tools, datasets + scale.
5. **Synthesis** — one Claude call clusters all extracts into a briefing,
   every claim cited back to source URLs.
6. **Export** — the briefing is rendered in-page and built into a `.docx`
   (OpenXML SDK) held in memory — nothing depends on local disk, which
   matters because Render's filesystem is ephemeral.

## Run locally (Visual Studio)

1. Open `Groundwork.csproj` in Visual Studio 2022 (or `dotnet run` from CLI).
2. Set the API key before running:
   - Windows (PowerShell): `$env:ANTHROPIC_API_KEY = "sk-ant-..."`
   - Or add it to *Debug > General > Environment variables* in project
     properties. **Do not** put it in `appsettings.json` if the repo is
     going to GitHub.
3. Browse to the app; fill in the brief and seed topics; run.

## Deploy: GitHub → Render

1. Push this folder to a GitHub repo (the `.gitignore` excludes build output).
2. In Render: **New → Web Service → Build from a Git repository**, pick the
   repo. Render detects the `Dockerfile` automatically.
3. Under **Environment**, add `ANTHROPIC_API_KEY` as a secret.
4. Deploy. Render sets `PORT`; the Dockerfile binds to it.

### Render notes

- **Free tier** spins down when idle — first hit after a gap takes ~30s.
- **Single instance only** for SignalR (no sticky-session concerns).
- Reports persist in PostgreSQL with their runs (v2.0-A); download any time.

## Database (v2.0-A)

Durable state lives in PostgreSQL (GW-HLD-SA-v2.0, P5). Runs, sources,
briefings, and generated Word documents persist; reports download after
restarts and redeploys.

- **Render**: provision a Render PostgreSQL instance; its `DATABASE_URL`
  is injected automatically when linked to the web service (or add it
  manually under Environment). The app translates the URL form for Npgsql
  and applies EF Core migrations at startup, before serving traffic.
- **Local**: either set `DATABASE_URL` to your Render database's external
  connection string, or run local PostgreSQL and set
  `ConnectionStrings:Groundwork` in `appsettings.Development.json`.
- **Migrations**: after changing entities, from the project folder:
  `dotnet ef migrations add <Name>` (requires `dotnet tool install --global dotnet-ef`).
  Migrations apply automatically at startup.
- **Restart behaviour**: runs interrupted by a restart are marked
  `interrupted` in history rather than vanishing.

## Configuration

| Setting | Where | Default |
|---|---|---|
| `ANTHROPIC_API_KEY` | environment variable / Render secret | required |
| `Anthropic:Model` | `appsettings.json` | `claude-sonnet-4-6` |
| `DATABASE_URL` | environment variable / Render (linked database) | required |
| `GROUNDWORK_ACCESS_KEY` | environment variable / Render secret — the shared access phrase (v2.0-D) | required |

## Notes on responsible use

- The fetcher only reads URLs that discovery proposes or you provide;
  it sends a clear user agent and makes at most 3 concurrent requests.
- Government/open-data sources are the cleanest inputs; prefer them where
  they exist. Respect site terms for anything you point it at.

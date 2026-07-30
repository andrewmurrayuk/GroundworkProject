// Groundwork project workspace: loads the project's current brief and seeds,
// starts runs, renders live pipeline progress over SignalR, run history,
// and the synthesised report.

(function () {
  const projectId = window.location.pathname.split("/").filter(Boolean).pop();
  const historyList = document.getElementById("history-list");
  const historyEmpty = document.getElementById("history-empty");
  const runButton = document.getElementById("run");
  const statusLine = document.getElementById("status-line");
  const errorLine = document.getElementById("error-line");
  const ledger = document.getElementById("ledger");
  const ledgerEmpty = document.getElementById("ledger-empty");
  const reportPanel = document.getElementById("report-panel");
  const reportTitle = document.getElementById("report-title");
  const reportBody = document.getElementById("report-body");
  const downloadLink = document.getElementById("download");

  const stageLabels = {
    Discovered: "discovered",
    Fetching: "fetching…",
    Fetched: "fetched",
    Extracting: "extracting…",
    Extracted: "extracted",
    Failed: "failed",
  };

  let connection = null;

  async function ensureConnection() {
    if (connection) return connection;
    connection = new signalR.HubConnectionBuilder()
      .withUrl("/hubs/pipeline")
      .withAutomaticReconnect()
      .build();

    connection.on("JobStatus", (msg) => (statusLine.textContent = msg));
    connection.on("JobFailed", (msg) => {
      statusLine.textContent = msg;
      runButton.disabled = false;
      runButton.textContent = "Start the research";
      loadHistory();
    });
    connection.on("SourceAdded", renderSource);
    connection.on("SourceUpdated", renderSource);
    connection.on("ReportReady", renderReport);

    await connection.start();
    return connection;
  }

  function esc(text) {
    const div = document.createElement("div");
    div.textContent = text ?? "";
    return div.innerHTML;
  }

  function renderSource(source) {
    ledgerEmpty.hidden = true;
    let row = document.getElementById("src-" + source.id);
    if (!row) {
      row = document.createElement("li");
      row.className = "ledger-row";
      row.id = "src-" + source.id;
      ledger.appendChild(row);
    }
    const stage = source.stage;
    row.innerHTML =
      `<span class="src-title">${esc(source.title || source.url)}</span>` +
      `<span class="stage-chip stage-${esc(stage)}">${esc(stageLabels[stage] || stage)}</span>` +
      (source.origin === "paper"
        ? `<span class="src-url">uploaded paper</span>`
        : `<span class="src-url">${esc(source.url)}</span>`) +
      (source.rationale ? `<span class="src-rationale">${esc(source.rationale)}</span>` : "") +
      (source.error ? `<span class="src-error">${esc(source.error)}</span>` : "");
  }

  function sourcesLine(urls) {
    if (!urls || urls.length === 0) return "";
    const links = urls
      .map((u) => {
        let host = u;
        try { host = new URL(u).host; } catch {}
        return `<a href="${esc(u)}" target="_blank" rel="noopener">${esc(host)}</a>`;
      })
      .join(" · ");
    return `<p class="report-sources">Sources: ${links}</p>`;
  }

  function listSection(title, items, renderItem) {
    if (!items || items.length === 0) return "";
    return `<h3>${esc(title)}</h3>` + items.map(renderItem).join("");
  }

  function bulletSection(title, items) {
    if (!items || items.length === 0) return "";
    return (
      `<h3>${esc(title)}</h3><ul>` +
      items.map((i) => `<li>${esc(i)}</li>`).join("") +
      `</ul>`
    );
  }

  function renderReport(payload) {
    const r = payload.report;
    statusLine.textContent = "Briefing complete.";
    runButton.disabled = false;
    runButton.textContent = "Run again";

    reportTitle.textContent = r.title || "Research briefing";
    downloadLink.href = payload.downloadUrl;

    reportBody.innerHTML =
      `<h3>Executive summary</h3><p>${esc(r.executiveSummary)}</p>` +
      listSection("Themes", r.themes, (t) =>
        `<h4>${esc(t.heading)}</h4><p>${esc(t.narrative)}</p>${sourcesLine(t.sourceUrls)}`) +
      listSection("Organisations active in this space", r.organisations, (o) =>
        `<h4>${esc(o.name)}</h4><p>${esc(o.whatTheyDo)}</p>${sourcesLine(o.sourceUrls)}`) +
      listSection("Tools and technology in use", r.toolsAndTech, (t) =>
        `<h4>${esc(t.name)}</h4><p>${esc(t.context)}</p>${sourcesLine(t.sourceUrls)}`) +
      listSection("Datasets", r.datasets, (d) =>
        `<h4>${esc(d.name)}</h4><p>${esc(d.details)}</p>${sourcesLine(d.sourceUrls)}`) +
      bulletSection("Gaps and opportunities", r.gapsAndOpportunities) +
      bulletSection("Suggested next steps", r.suggestedNextSteps);

    reportPanel.hidden = false;
    reportPanel.scrollIntoView({ behavior: "smooth", block: "start" });
    loadHistory();
  }

  function fmtDateTime(iso) {
    const d = new Date(iso);
    return d.toLocaleDateString(undefined, { day: "numeric", month: "short" }) +
      " " + d.toLocaleTimeString(undefined, { hour: "2-digit", minute: "2-digit" });
  }

  const statusClass = {
    complete: "Extracted",
    failed: "Failed",
    interrupted: "Failed",
  };

  async function loadProject() {
    try {
      const response = await fetch("/api/projects/" + projectId);
      if (response.status === 401) { window.location.href = "/login"; return; }
      if (!response.ok) throw new Error();
      const p = await response.json();
      document.getElementById("project-title").textContent = p.name;
      document.getElementById("project-subtitle").textContent = p.description || "";
      document.title = p.name + " · Groundwork";
      if (!document.getElementById("brief").value) document.getElementById("brief").value = p.brief;
      if (!document.getElementById("topics").value) document.getElementById("topics").value = p.seeds;
    } catch {
      document.getElementById("project-title").textContent = "Project not found";
    }
  }

  async function loadHistory() {
    try {
      const response = await fetch(`/api/projects/${projectId}/runs`);
      const runs = await response.json();
      historyEmpty.hidden = runs.length > 0;
      historyList.innerHTML = "";
      for (const r of runs) {
        const li = document.createElement("li");
        li.className = "history-row";
        const chipClass = statusClass[r.status] || "Fetching";
        li.innerHTML =
          `<span class="history-when">${esc(fmtDateTime(r.createdUtc))}</span>` +
          `<span class="stage-chip stage-${chipClass}">${esc(r.status)}</span>` +
          `<span class="history-meta">${r.sourceCount} source${r.sourceCount === 1 ? "" : "s"}</span>` +
          (r.hasReport
            ? `<a class="history-download" href="/api/report/${r.id}">report</a>`
            : `<span class="history-meta">${esc(r.failureMessage || "")}</span>`);
        historyList.appendChild(li);
      }
    } catch { /* history is non-critical */ }
  }

  const draftButton = document.getElementById("draft");
  const draftError = document.getElementById("draft-error");

  draftButton.addEventListener("click", async () => {
    draftError.hidden = true;
    const topic = document.getElementById("topic").value.trim();
    if (!topic) {
      draftError.textContent = "Name a topic first \u2014 a short phrase is enough.";
      draftError.hidden = false;
      return;
    }

    const briefField = document.getElementById("brief");
    const topicsField = document.getElementById("topics");
    if ((briefField.value.trim() || topicsField.value.trim()) &&
        !confirm("Replace the current brief and seed topics with a new draft?")) {
      return;
    }

    draftButton.disabled = true;
    draftButton.textContent = "Drafting\u2026";
    try {
      const response = await fetch(`/api/projects/${projectId}/suggest`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ topic }),
      });
      const data = await response.json();
      if (!response.ok) throw new Error(data.error || data.detail || "Drafting failed.");
      briefField.value = data.brief || "";
      topicsField.value = (data.topics || []).join("\n");
      briefField.focus();
    } catch (err) {
      draftError.textContent = err.message;
      draftError.hidden = false;
    } finally {
      draftButton.disabled = false;
      draftButton.textContent = "Draft the brief";
    }
  });

  const paperList = document.getElementById("paper-list");
  const paperError = document.getElementById("paper-error");
  const paperFile = document.getElementById("paper-file");
  let papers = [];

  async function loadPapers() {
    try {
      const response = await fetch(`/api/projects/${projectId}/papers`);
      papers = await response.json();
      paperList.innerHTML = "";
      for (const p of papers) {
        const li = document.createElement("li");
        li.className = "paper-row";
        li.innerHTML =
          `<label class="paper-pick"><input type="checkbox" value="${p.id}" data-ok="${p.extractionOk}" />` +
          `<span class="paper-name">${esc(p.fileName)}</span></label>` +
          (p.extractionOk ? "" : `<span class="paper-flag" title="Little readable text was extracted — likely scanned or image-only">poor text</span>`) +
          `<button class="paper-remove" data-id="${p.id}" title="Remove paper">remove</button>`;
        paperList.appendChild(li);
      }
    } catch { /* paper library is non-critical to render */ }
  }

  paperFile.addEventListener("change", async () => {
    paperError.hidden = true;
    const file = paperFile.files[0];
    if (!file) return;
    const form = new FormData();
    form.append("file", file);
    try {
      const response = await fetch(`/api/projects/${projectId}/papers`, {
        method: "POST",
        body: form,
      });
      const data = await response.json();
      if (!response.ok) throw new Error(data.error || "Upload failed.");
      await loadPapers();
    } catch (err) {
      paperError.textContent = err.message;
      paperError.hidden = false;
    } finally {
      paperFile.value = "";
    }
  });

  paperList.addEventListener("click", async (e) => {
    const button = e.target.closest(".paper-remove");
    if (!button) return;
    if (!confirm("Remove this paper from the project?")) return;
    await fetch(`/api/projects/${projectId}/papers/${button.dataset.id}`, { method: "DELETE" });
    await loadPapers();
  });

  function selectedPapers() {
    return [...paperList.querySelectorAll("input[type=checkbox]:checked")];
  }

  runButton.addEventListener("click", async () => {
    errorLine.hidden = true;
    const brief = document.getElementById("brief").value.trim();
    const topics = document.getElementById("topics").value.trim();
    const urls = document.getElementById("urls").value.trim();

    if (!brief) {
      errorLine.textContent = "Write a research brief first — it steers everything downstream.";
      errorLine.hidden = false;
      return;
    }
    const picked = selectedPapers();
    const paperIds = picked.map((c) => c.value);
    if (!topics && !urls && paperIds.length === 0) {
      errorLine.textContent = "Add seed topics, URLs, or select papers.";
      errorLine.hidden = false;
      return;
    }
    const flagged = picked.filter((c) => c.dataset.ok === "false").length;
    if (flagged > 0 &&
        !confirm(`${flagged} selected paper${flagged === 1 ? " has" : "s have"} poor extracted text and may add little to the briefing. Include anyway?`)) {
      return;
    }

    runButton.disabled = true;
    runButton.textContent = "Working…";
    ledger.innerHTML = "";
    ledgerEmpty.hidden = false;
    reportPanel.hidden = true;
    statusLine.textContent = "Starting…";

    try {
      const conn = await ensureConnection();
      const response = await fetch(`/api/projects/${projectId}/runs`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ brief, topics, urls, paperIds }),
      });
      const data = await response.json();
      if (!response.ok) throw new Error(data.error || "The run could not start.");
      await conn.invoke("JoinJob", data.jobId);
    } catch (err) {
      errorLine.textContent = err.message;
      errorLine.hidden = false;
      runButton.disabled = false;
      runButton.textContent = "Start the research";
      statusLine.textContent = "Waiting for a brief.";
    }
  });
  loadProject();
  loadHistory();
  loadPapers();
})();

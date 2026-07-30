// Projects list: create projects and open their workspaces.

(function () {
  const list = document.getElementById("project-list");
  const empty = document.getElementById("projects-empty");
  const status = document.getElementById("projects-status");
  const createButton = document.getElementById("create-project");
  const createError = document.getElementById("create-error");

  function esc(text) {
    const div = document.createElement("div");
    div.textContent = text ?? "";
    return div.innerHTML;
  }

  function fmtDate(iso) {
    if (!iso) return "";
    const d = new Date(iso);
    return d.toLocaleDateString(undefined, { day: "numeric", month: "short", year: "numeric" });
  }

  async function load() {
    try {
      const response = await fetch("/api/projects");
      if (response.status === 401) { window.location.href = "/login"; return; }
      const projects = await response.json();
      list.innerHTML = "";
      empty.hidden = projects.length > 0;
      status.textContent = projects.length === 1
        ? "1 project"
        : projects.length + " projects";

      for (const p of projects) {
        const li = document.createElement("li");
        li.className = "project-row";
        li.innerHTML =
          `<a class="project-link" href="/projects/${p.id}">` +
          `<span class="project-name">${esc(p.name)}</span>` +
          (p.description ? `<span class="project-desc">${esc(p.description)}</span>` : "") +
          `<span class="project-meta">${p.runCount} run${p.runCount === 1 ? "" : "s"}` +
          (p.lastRunUtc ? ` · last ${fmtDate(p.lastRunUtc)}` : "") +
          ` · created ${fmtDate(p.createdUtc)}</span>` +
          `</a>` +
          `<button class="archive-button" data-id="${p.id}" title="Archive project">Archive</button>`;
        list.appendChild(li);
      }
    } catch {
      status.textContent = "Could not load projects.";
    }
  }

  list.addEventListener("click", async (e) => {
    const button = e.target.closest(".archive-button");
    if (!button) return;
    if (!confirm("Archive this project? It disappears from the list; its data is kept.")) return;
    await fetch("/api/projects/" + button.dataset.id, { method: "DELETE" });
    await load();
  });

  createButton.addEventListener("click", async () => {
    createError.hidden = true;
    const name = document.getElementById("project-name").value.trim();
    const description = document.getElementById("project-description").value.trim();
    if (!name) {
      createError.textContent = "Give the project a name.";
      createError.hidden = false;
      return;
    }
    createButton.disabled = true;
    try {
      const response = await fetch("/api/projects", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ name, description }),
      });
      const data = await response.json();
      if (!response.ok) throw new Error(data.error || "Could not create the project.");
      window.location.href = "/projects/" + data.id;
    } catch (err) {
      createError.textContent = err.message;
      createError.hidden = false;
      createButton.disabled = false;
    }
  });

  load();
})();

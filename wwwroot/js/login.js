// Access gate (2.0-D): one shared phrase, remembered by cookie.

(function () {
  const button = document.getElementById("enter");
  const field = document.getElementById("access-key");
  const error = document.getElementById("auth-error");

  async function submit() {
    error.hidden = true;
    const key = field.value;
    if (!key) {
      error.textContent = "Enter the access phrase.";
      error.hidden = false;
      return;
    }
    button.disabled = true;
    try {
      const response = await fetch("/api/auth", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ key }),
      });
      if (!response.ok) {
        error.textContent = "That phrase isn't right.";
        error.hidden = false;
        button.disabled = false;
        field.select();
        return;
      }
      const params = new URLSearchParams(window.location.search);
      window.location.href = params.get("ReturnUrl") || "/";
    } catch {
      error.textContent = "Could not reach the server. Try again.";
      error.hidden = false;
      button.disabled = false;
    }
  }

  button.addEventListener("click", submit);
  field.addEventListener("keydown", (e) => { if (e.key === "Enter") submit(); });
})();

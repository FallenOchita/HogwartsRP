(() => {
  const signedOut = document.getElementById("signed-out");
  const signedIn = document.getElementById("signed-in");
  const message = document.getElementById("message");
  let csrfToken = "";

  async function loadLinks() {
    const response = await fetch("/account/links", {
      credentials: "same-origin",
      cache: "no-store"
    });
    if (!response.ok) throw new Error("Could not load account links");
    const links = await response.json();
    if (!Array.isArray(links)) throw new Error("Invalid account-link response");
    const allowed = new Set(["discord", "steam", "epic"]);
    for (const link of links) {
      if (!link || !allowed.has(link.provider) || typeof link.connected !== "boolean") {
        throw new Error("Invalid account-link entry");
      }
      const state = document.querySelector('[data-provider-state="' + link.provider + '"]');
      const button = document.querySelector('[data-unlink="' + link.provider + '"]');
      if (!state || !button) continue;
      state.textContent = link.connected ? "Connected" : "Not connected";
      button.hidden = !link.connected;
    }
  }

  async function loadSession() {
    try {
      const response = await fetch("/account/session", {
        credentials: "same-origin",
        cache: "no-store"
      });
      if (!response.ok) {
        signedOut.hidden = false;
        signedIn.hidden = true;
        return;
      }
      const session = await response.json();
      if (typeof session.accountId !== "string" || typeof session.csrfToken !== "string") {
        throw new Error("Invalid session response");
      }
      csrfToken = session.csrfToken;
      document.getElementById("account-id").textContent = "Account ID: " + session.accountId;
      const expiry = new Date(session.expiresAt * 1000);
      document.getElementById("expiry").textContent = "Session expires: " + expiry.toLocaleString();
      signedOut.hidden = true;
      signedIn.hidden = false;
      await loadLinks();
    } catch (_) {
      message.textContent = "Could not load your account information. Refresh the page to try again.";
    }
  }

  document.querySelectorAll("[data-unlink]").forEach((button) => {
    button.addEventListener("click", async () => {
      const provider = button.dataset.unlink;
      if (!new Set(["discord", "steam", "epic"]).has(provider)) return;
      button.disabled = true;
      message.textContent = "";
      try {
        const response = await fetch("/account/links/" + provider, {
          method: "DELETE",
          credentials: "same-origin",
          headers: { "X-HogwartsRP-CSRF": csrfToken }
        });
        if (!response.ok) throw new Error("Disconnect failed");
        await loadLinks();
        message.textContent = "The " + provider + " connection was updated.";
      } catch (_) {
        message.textContent = "That account could not be disconnected. Please try again.";
      } finally {
        button.disabled = false;
      }
    });
  });

  document.getElementById("logout").addEventListener("click", async () => {
    const button = document.getElementById("logout");
    button.disabled = true;
    message.textContent = "";
    try {
      const response = await fetch("/account/logout", {
        method: "POST",
        credentials: "same-origin",
        headers: { "X-HogwartsRP-CSRF": csrfToken }
      });
      if (!response.ok) throw new Error("Sign-out failed");
      csrfToken = "";
      signedIn.hidden = true;
      signedOut.hidden = false;
      message.textContent = "You have been signed out.";
    } catch (_) {
      message.textContent = "Sign-out could not be completed. Please try again.";
    } finally {
      button.disabled = false;
    }
  });

  loadSession();
})();

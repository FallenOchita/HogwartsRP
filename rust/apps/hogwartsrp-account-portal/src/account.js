    (() => {
      const signedOut = document.getElementById("signed-out");
      const signedIn = document.getElementById("signed-in");
      const message = document.getElementById("message");
      let csrfToken = "";

      async function loadSession() {
        try {
          const response = await fetch("/account/session", { credentials: "same-origin", cache: "no-store" });
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
        } catch (_) {
          message.textContent = "Could not check your sign-in status. Refresh the page to try again.";
        }
      }

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

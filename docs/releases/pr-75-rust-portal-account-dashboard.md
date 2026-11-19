# PR #76 — Rust portal account dashboard

## Summary

- Serve the central account dashboard at `/` and `/account`.
- Show sign-in state, opaque HogwartsRP account ID, and session expiry.
- Add same-origin JavaScript for session loading and CSRF-protected sign-out.
- Add a restrictive Content Security Policy, no-store responses, no-sniff headers, and frame protection.
- Present Discord, Steam, and Epic Games as optional connections while clearly marking provider-link UI integration as pending; no fake connection states or nonfunctional connect buttons.

## Boundaries

The dashboard uses the central Rust browser session and does not change per-game-server OIDC or join-ticket behavior. Provider verification handlers exist separately but are not yet integrated into this Rust portal. No provider credentials or tokens are exposed to the browser.

The portal remains a development deployment with process-local sessions and JSON identity storage. Production database/session infrastructure, provider-link integration, rate limits, monitoring, and deployment hardening remain follow-up work.

## Validation

CI must pass the Rust formatting and workspace tests on Ubuntu and Windows before merge. No passing result is claimed until CI completes.

Shared CHANGELOG.md and UPDATES.md are intentionally not edited on the feature branch to avoid recurring merge conflicts; consolidate this release note after merge.

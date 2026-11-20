# Rust launcher public server browser

## Added

- Added a native Rust directory API client for the existing `GET /v1/servers` endpoint.
- Added public server discovery to the eframe/egui launcher, including name search, bounded pagination, online/offline state, and player counts.
- Added explicit selection of a directory listing to populate the existing server profile fields.
- Added response-size limits, request timeouts, URL checks, and validation of listing metadata before displaying server records.
- Added `HOGWARTSRP_DIRECTORY_URL` configuration; local development defaults to `http://127.0.0.1:8790`.

## Scope and limitations

- The directory service from PR #86 must be running and reachable. This change does not deploy or host it.
- Selecting a listing only fills the existing server profile. It does not implement OIDC join authentication, authenticated WebSocket sessions, or actual game-server joining.
- The server's public endpoint is not contacted merely by loading or selecting a listing. The existing manual status-check action remains separate.
- No live directory deployment or end-to-end public listing test is claimed.

## Validation

- Unit tests cover directory response deserialization, URL rejection, and invalid listing rejection.
- Hosted CI must pass before merge.

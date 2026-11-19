# Roadmap

## Completed baseline
- Six department panels and 21 ticket categories
- Modal-based intake and private ticket channels
- Claim/unclaim, assign/unassign, review, escalation and closure
- Application/whitelist approve, deny and revision decisions with audit trail
- MongoDB persistence, ticket audit records and statistics
- HTML/Markdown transcript generation
- Persistent ticket quotas and cooldown state
- Docker Compose and operational documentation

## Production hardening before public launch
- Automated tests for permission matrices, race conditions and transcript escaping
- CI pipeline for linting, tests, dependency audit and container build
- Transcript upload retry queue and size-aware chunking/archival
- Channel/database reconciliation worker for partial Discord/API failures
- Dashboard pagination and filters for large review queues
- Structured metrics and alerting
- Explicit retention and privacy workflows
- Multi-guild configuration and per-guild staff-role mappings
- Optional Redis-backed distributed interaction throttling if deployment needs very high throughput
- Integration API for approved whitelist and character application decisions

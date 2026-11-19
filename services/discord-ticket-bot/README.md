# HogwartsRP Ticket Bot

Discord.js v14 ticket, moderation appeal, character application, whitelist and development-support bot. Requires Node.js 22+ and MongoDB 7+.

## Features
- Six department panels and 21 ticket types.
- Modal intake for appeals, character/whitelist applications, bug reports and player/staff reports.
- Private channels with opener, department staff and bot access.
- Claim, unclaim, review, escalate, close and application decision actions.
- MongoDB ticket records, audit trail, statistics and user history.
- HTML and Markdown transcripts, persisted in a Docker volume and uploaded to the transcript log channel.
- Three-open-ticket limit, five-minute cooldown and same-type duplicate prevention.
- Docker Compose deployment.

## Quick start
1. Create a Discord application and bot in the Developer Portal.
2. Invite with bot and applications.commands scopes. Grant View Channels, Send Messages, Embed Links, Attach Files, Read Message History and Manage Channels. Avoid Administrator.
3. Copy .env.example to .env and configure tokens, Mongo URI, category IDs, role IDs and log channels.
4. Install dependencies with npm install.
5. Deploy commands with npm run deploy:commands.
6. Start with npm start, or use docker compose up -d --build.
7. Run /setup-tickets as a server administrator.

## Configuration
DISCORD_TOKEN, CLIENT_ID and MONGO_URI are required. GUILD_ID is optional for fast guild-only command deployment. Configure six category IDs, department role IDs, AUDIT_CHANNEL, TICKET_LOG_CHANNEL, MAX_OPEN_TICKETS, TICKET_COOLDOWN_MS and TRANSCRIPT_DIR.

## Commands
/setup-tickets, /ticket-stats, /close-ticket, /claim-ticket, /user-history and /staff-review-queue.

## Operations and security
Back up MongoDB and transcript storage together. Keep credentials in a secret manager. Restrict transcript channel visibility. Review bot permissions and staff roles. Never log modal answer bodies. HTML transcript content is escaped. Use a shared rate limiter before running multiple bot instances.

## Known boundaries
Application decisions are recorded in the audit log but do not yet call a separate whitelist/character service. Cooldowns are in-memory and per process. Assignment/reassignment UI, transcript retry queue, dashboard pagination, automated channel reconciliation and multi-instance rate limiting are future hardening tasks. The service is a production-oriented baseline, not a claim of completed live-guild validation.

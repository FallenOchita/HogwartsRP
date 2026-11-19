# Architecture Overview

The bot is a single Node.js service with a modular Discord interaction layer and MongoDB persistence.

- Discord.js gateway: receives slash commands, select menus, modal submissions and buttons.
- Command handlers: administrative commands with runtime authorization checks.
- Interaction handlers: ticket select, intake modal and ticket control actions.
- Ticket manager: channel provisioning, assignment, claim and state transitions.
- Services: audit, statistics, transcript generation, panels and persistent user quotas.
- Mongoose models: Ticket, TicketStatistics, TicketAudit and UserTicketState.
- Transcript storage: HTML and Markdown files on a persistent volume, with copies uploaded to the restricted transcript channel.

MongoDB is the source of truth. Discord channels are provisioned resources associated with Ticket.channelId. UserTicketState atomically reserves open-ticket slots and cooldown windows; startup reconciliation repairs open counts from persisted Ticket documents.

The application uses one bot process by default. Persistent quota state supports multiple instances, but production horizontal scaling still requires operational safeguards around Discord rate limits, command deployment, and idempotent recovery.

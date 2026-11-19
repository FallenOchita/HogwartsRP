# MongoDB Schema

## Ticket
One record per ticket channel. Fields include ticketId, ticketType, userId, channelId, guildId, status, claimedBy, assignedStaff, priority, modal answers, response/closure timestamps, transcript paths, escalation and review metadata. Indexes support guild/user/status lookups and open queue listing.

## TicketStatistics
One record per guild. Counters: opened, closed, appealed, applications, reports and bugs. Average response and resolution times are aggregated from Ticket records.

## TicketAudit
Append-only application audit records containing guild, ticket, channel, actor, action, details and timestamp. Restrict database credentials to the bot service.

## Production indexes
Mongoose schema indexes are declared in code. Manage index creation deliberately during production deployment and verify them with MongoDB tooling. Back up MongoDB and transcript storage as a coordinated retention set.

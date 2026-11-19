# Developer Guide

## Layout
commands: slash commands; events: gateway event handlers; buttons/selectMenus/modals: interaction handlers; services: audit, statistics, panels and transcripts; managers: ticket lifecycle; models: Mongoose persistence; config: environment and ticket definitions.

## Adding a ticket type
Add a type definition in src/config/ticketTypes.js with unique ID, label, department group, staff department and priority. Add a modal schema in fields when custom questions are needed. Discord modals support at most five text inputs.

## Security rules
- Validate permissions on every interaction; default command permissions are not the authorization boundary.
- Resolve ticket ownership using the MongoDB record for the current channel, never channel names or topics alone.
- Escape user-generated transcript content before writing HTML.
- Never log modal answer bodies or transcript contents.
- Treat Discord API and MongoDB operations as fallible.
- Keep external character/whitelist mutations behind a separately reviewed integration.

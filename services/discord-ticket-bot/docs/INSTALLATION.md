# Installation Guide

## Discord setup
Create an application in Discord Developer Portal, add a bot, copy its token and application ID. Invite it with bot and applications.commands scopes. Required permissions: View Channels, Send Messages, Embed Links, Attach Files, Read Message History and Manage Channels. The current bot only subscribes to the Guilds gateway intent; no privileged message-content intent is required.

## Local deployment
Use Node.js 22+, install with npm install, copy .env.example to .env, configure MongoDB and Discord values, then run npm run deploy:commands and npm start.

## Docker deployment
Copy .env.example to .env and set values. Run docker compose up -d --build. MongoDB data and transcript files use persistent named volumes. Do not expose MongoDB publicly; use private networking and authentication for a production MongoDB deployment.

Set GUILD_ID during development for quick command registration. Omit it for global registration; global propagation may take time.

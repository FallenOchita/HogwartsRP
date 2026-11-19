const required = ["DISCORD_TOKEN", "CLIENT_ID", "MONGO_URI"];
for (const key of required) if (!process.env[key]) throw new Error("Missing required environment variable: " + key);
const ids = (value = "") => value.split(",").map(x => x.trim()).filter(Boolean);
module.exports = Object.freeze({
 token:process.env.DISCORD_TOKEN, clientId:process.env.CLIENT_ID, guildId:process.env.GUILD_ID||null,
 mongoUri:process.env.MONGO_URI, logChannelId:process.env.LOG_CHANNEL||process.env.TICKET_LOG_CHANNEL||null,
 auditChannelId:process.env.AUDIT_CHANNEL||null, transcriptChannelId:process.env.TICKET_LOG_CHANNEL||null,
 transcriptDir:process.env.TRANSCRIPT_DIR||"./transcripts", cooldownMs:Number(process.env.TICKET_COOLDOWN_MS||300000),
 maxOpenTickets:Number(process.env.MAX_OPEN_TICKETS||3), logLevel:process.env.LOG_LEVEL||"info",
 adminRoleIds:ids(process.env.ADMIN_ROLE_IDS),
 categories:{SUPPORT:process.env.SUPPORT_CATEGORY_ID||null,APPLICATIONS:process.env.APPLICATIONS_CATEGORY_ID||null,REPORTS:process.env.REPORTS_CATEGORY_ID||null,APPEALS:process.env.APPEALS_CATEGORY_ID||null,TECHNICAL:process.env.TECHNICAL_CATEGORY_ID||null,DEVELOPMENT:process.env.DEVELOPMENT_CATEGORY_ID||null},
 roles:{SUPPORT:process.env.SUPPORT_ROLE_ID||null,MODERATION:process.env.MODERATION_ROLE_ID||null,APPLICATION:process.env.APPLICATION_ROLE_ID||null,DEVELOPMENT:process.env.DEVELOPMENT_ROLE_ID||null}
});

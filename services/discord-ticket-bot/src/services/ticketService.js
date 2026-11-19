const {AttachmentBuilder,EmbedBuilder}=require("discord.js");const config=require("../config/env");
const manager=require("../managers/ticketManager");const transcripts=require("./transcriptService");const audit=require("./auditService");const logger=require("../utils/logger");
async function closeTicket({client,ticket,actorId,reason}){
 const channel=await client.channels.fetch(ticket.channelId);const files=await transcripts.generate(channel,ticket);
 ticket.transcriptPath=files.htmlPath;ticket.transcriptMarkdownPath=files.mdPath;await manager.close(client,ticket,actorId,reason);
 const logId=config.transcriptChannelId||config.logChannelId;
 if(logId){try{const log=await client.channels.fetch(logId);if(log?.isTextBased())await log.send({embeds:[new EmbedBuilder().setTitle("Ticket #"+ticket.ticketId+" closed").setDescription("Type: "+ticket.ticketType+"\nOpener: <@"+ticket.userId+">\nClosed by: <@"+actorId+">\nReason: "+reason).setColor(0x777777)],files:[new AttachmentBuilder(files.htmlPath),new AttachmentBuilder(files.mdPath)]});}catch(error){logger.error("Transcript upload failed",{ticketId:ticket.ticketId,message:error.message});}}
 await audit.record(client,{guildId:ticket.guildId,ticketId:ticket.ticketId,channelId:ticket.channelId,actorId,action:"TRANSCRIPT_GENERATED",details:{messages:files.messages}}).catch(error=>logger.error("Transcript audit write failed",{ticketId:ticket.ticketId,message:error.message}));
 await channel.send("This ticket has been closed. Transcript generation has completed.");
 await channel.permissionOverwrites.edit(ticket.userId,{SendMessages:false}).catch(()=>{});
 return files;
}
module.exports={closeTicket};

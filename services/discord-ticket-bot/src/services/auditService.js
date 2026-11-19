const TicketAudit=require("../models/TicketAudit");const config=require("../config/env");const logger=require("../utils/logger");
async function record(client,data){
 await TicketAudit.create(data);
 const id=config.auditChannelId||config.logChannelId;if(!id)return;
 try{const channel=await client.channels.fetch(id);if(channel?.isTextBased())await channel.send({content:"**Ticket Audit — "+data.action+"**\nTicket: #"+data.ticketId+"\nActor: "+(data.actorId?"<@"+data.actorId+">":"System")+"\n"+JSON.stringify(data.details||{}).slice(0,1000)});}
 catch(error){logger.warn("Audit channel delivery failed",{message:error.message});}
}
module.exports={record};

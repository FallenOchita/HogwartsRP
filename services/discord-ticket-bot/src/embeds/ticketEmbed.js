const {EmbedBuilder,ActionRowBuilder,ButtonBuilder,ButtonStyle}=require("discord.js");
function opened(ticket,type){
 const embed=new EmbedBuilder().setColor(0x5b4b8a).setTitle(type.label+" — #"+ticket.ticketId).setDescription("A staff member will assist you. Please keep relevant information in this channel.").addFields(
 {name:"Opened by",value:"<@"+ticket.userId+">",inline:true},{name:"Priority",value:ticket.priority,inline:true},{name:"Status",value:"Open",inline:true});
 for(const [key,value] of ticket.answers)embed.addFields({name:key.replace(/_/g," ").slice(0,256),value:String(value).slice(0,1024)||"—"});
 return embed;
}
function controls(ticketType){
 const rows=[new ActionRowBuilder().addComponents(
  new ButtonBuilder().setCustomId("ticket:claim").setLabel("Claim").setStyle(ButtonStyle.Primary),
  new ButtonBuilder().setCustomId("ticket:unclaim").setLabel("Unclaim").setStyle(ButtonStyle.Secondary),
  new ButtonBuilder().setCustomId("ticket:review").setLabel("Request Review").setStyle(ButtonStyle.Secondary),
  new ButtonBuilder().setCustomId("ticket:escalate").setLabel("Escalate").setStyle(ButtonStyle.Danger),
  new ButtonBuilder().setCustomId("ticket:close").setLabel("Close").setStyle(ButtonStyle.Success))];
 if(["character-application","whitelist-application","character-appeal"].includes(ticketType))rows.push(new ActionRowBuilder().addComponents(
  new ButtonBuilder().setCustomId("ticket:approve").setLabel("Approve").setStyle(ButtonStyle.Success),
  new ButtonBuilder().setCustomId("ticket:deny").setLabel("Deny").setStyle(ButtonStyle.Danger),
  new ButtonBuilder().setCustomId("ticket:revision").setLabel("Request Revision").setStyle(ButtonStyle.Secondary)));
 return rows;
}
module.exports={opened,controls};

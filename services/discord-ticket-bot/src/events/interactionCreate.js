const {Events}=require("discord.js");const logger=require("../utils/logger");
const {buildModal}=require("../selectMenus/ticketSelect");const ticketCreate=require("../modals/ticketCreate");const ticketControls=require("../buttons/ticketControls");
module.exports={name:Events.InteractionCreate,async execute(i){
 try{
  if(i.isChatInputCommand()){const command=i.client.commands.get(i.commandName);if(command)await command.execute(i);return;}
  if(i.isStringSelectMenu()&&i.customId==="ticket:select"){
   const typeId=i.values[0];return i.showModal(buildModal(typeId));
  }
  if(i.isModalSubmit()&&i.customId.startsWith("ticket:create:"))return ticketCreate.execute(i);
  if(i.isButton()&&i.customId.startsWith("ticket:"))return ticketControls.execute(i);
 }catch(error){
  logger.error("Interaction failed",{name:error.name,message:error.message,stack:error.stack});
  const payload={content:error.message?.slice(0,1800)||"An unexpected error occurred.",ephemeral:true};
  if(i.deferred||i.replied)await i.followUp(payload).catch(()=>{});else await i.reply(payload).catch(()=>{});
 }
}};

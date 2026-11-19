const manager=require("../managers/ticketManager");const config=require("../config/env");const Ticket=require("../models/Ticket");
const cooldowns=new Map();
async function execute(i){
 const typeId=i.customId.slice("ticket:create:".length);
 const type=require("../config/ticketTypes").types[typeId];if(!type)return i.reply({content:"Invalid ticket type.",ephemeral:true});
 const last=cooldowns.get(i.user.id)||0,remaining=config.cooldownMs-(Date.now()-last);
 if(remaining>0)return i.reply({content:"Please wait "+Math.ceil(remaining/60000)+" minute(s) before opening another ticket.",ephemeral:true});
 const answers={};for(const row of i.components)for(const component of row.components)answers[component.customId]=i.fields.getTextInputValue(component.customId);
 await i.deferReply({ephemeral:true});
 const result=await manager.createTicket({client:i.client,guild:i.guild,user:i.user,typeId,answers});
 cooldowns.set(i.user.id,Date.now());
 return i.editReply("Your ticket has been created: "+result.channel);
}
module.exports={execute};

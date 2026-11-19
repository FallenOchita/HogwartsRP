const manager=require("../managers/ticketManager");
async function execute(i){
 const typeId=i.customId.slice("ticket:create:".length);
 const type=require("../config/ticketTypes").types[typeId];if(!type)return i.reply({content:"Invalid ticket type.",ephemeral:true});
 const answers={};for(const row of i.components)for(const component of row.components)answers[component.customId]=i.fields.getTextInputValue(component.customId);
 await i.deferReply({ephemeral:true});
 const result=await manager.createTicket({client:i.client,guild:i.guild,user:i.user,typeId,answers});
 return i.editReply("Your ticket has been created: "+result.channel);
}
module.exports={execute};

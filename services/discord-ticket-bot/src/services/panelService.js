const {EmbedBuilder,ActionRowBuilder,StringSelectMenuBuilder}=require("discord.js");const {types}=require("../config/ticketTypes");
async function createPanels(guild){
 const groups=["SUPPORT","APPLICATIONS","REPORTS","APPEALS","TECHNICAL","DEVELOPMENT"],created=[];
 for(const group of groups){
  const name="open-"+group.toLowerCase()+"-ticket";let channel=guild.channels.cache.find(c=>c.name===name&&c.isTextBased());
  if(!channel)channel=await guild.channels.create({name,reason:"HogwartsRP ticket panel setup"});
  const options=Object.values(types).filter(t=>t.group===group).map(t=>({label:t.label,value:t.id,description:t.description.slice(0,100)}));
  const row=new ActionRowBuilder().addComponents(new StringSelectMenuBuilder().setCustomId("ticket:select").setPlaceholder("Choose a ticket type…").addOptions(options));
  await channel.send({embeds:[new EmbedBuilder().setColor(0x5b4b8a).setTitle("HogwartsRP — "+group+" Tickets").setDescription("Select the ticket type that best matches your request. A private channel and short form will be created.")],components:[row]});
  created.push(channel);
 }
 return created;
}
module.exports={createPanels};

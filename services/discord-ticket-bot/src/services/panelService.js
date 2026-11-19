const {EmbedBuilder,ActionRowBuilder,StringSelectMenuBuilder,ChannelType}=require("discord.js");const {types}=require("../config/ticketTypes");const config=require("../config/env");
async function ensureCategories(guild){
 for(const group of ["SUPPORT","APPLICATIONS","REPORTS","APPEALS","TECHNICAL","DEVELOPMENT"]){
  let category=config.categories[group]?await guild.channels.fetch(config.categories[group]).catch(()=>null):null;
  if(category&&category.type!==ChannelType.GuildCategory)throw new Error("Configured "+group+" category ID is not a category channel.");
  if(!category){
   category=guild.channels.cache.find(c=>c.type===ChannelType.GuildCategory&&c.name===group.toLowerCase());
   if(!category)category=await guild.channels.create({name:group.toLowerCase(),type:ChannelType.GuildCategory,reason:"HogwartsRP ticket category setup"});
   config.categories[group]=category.id;
  }
 }
}
async function createPanels(guild){
 const groups=["SUPPORT","APPLICATIONS","REPORTS","APPEALS","TECHNICAL","DEVELOPMENT"],created=[];
 await ensureCategories(guild);
 for(const group of groups){
  const name="open-"+group.toLowerCase()+"-ticket";let channel=guild.channels.cache.find(c=>c.name===name&&c.isTextBased());
  if(!channel)channel=await guild.channels.create({name,reason:"HogwartsRP ticket panel setup"});
  const options=Object.values(types).filter(t=>t.group===group).map(t=>({label:t.label,value:t.id,description:t.description.slice(0,100)}));
  const row=new ActionRowBuilder().addComponents(new StringSelectMenuBuilder().setCustomId("ticket:select").setPlaceholder("Choose a ticket type…").addOptions(options));
  const embed=new EmbedBuilder().setColor(0x5b4b8a).setTitle("HogwartsRP — "+group+" Tickets").setDescription("Select the ticket type that best matches your request. A private channel and short form will be created.");
  const recent=await channel.messages.fetch({limit:50});
  const existing=recent.find(m=>m.author.id===guild.client.user.id&&m.embeds.some(e=>e.title===embed.data.title));
  if(existing)await existing.edit({embeds:[embed],components:[row]});else await channel.send({embeds:[embed],components:[row]});
  created.push(channel);
 }
 return created;
}
module.exports={createPanels};

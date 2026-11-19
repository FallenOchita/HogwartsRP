const {SlashCommandBuilder,EmbedBuilder,PermissionFlagsBits}=require("discord.js");const stats=require("../services/statisticsService");const {isAdmin}=require("../utils/permissions");
const duration=ms=>ms?Math.round(ms/60000)+" min":"Not available";
module.exports={data:new SlashCommandBuilder().setName("ticket-stats").setDescription("View ticket statistics").setDefaultMemberPermissions(PermissionFlagsBits.ManageGuild),async execute(i){
 if(!isAdmin(i.member))return i.reply({content:"Administrator access required.",ephemeral:true});const s=await stats.get(i.guildId);
 const embed=new EmbedBuilder().setTitle("HogwartsRP Ticket Statistics").setColor(0x5b4b8a).addFields(
 {name:"Open Tickets",value:String(s.open),inline:true},{name:"Closed Tickets",value:String(s.closed),inline:true},{name:"Total Opened",value:String(s.opened||0),inline:true},
 {name:"Average Response",value:duration(s.avgResponseMs),inline:true},{name:"Average Resolution",value:duration(s.avgResolutionMs),inline:true},
 {name:"Appeals / Applications / Reports / Bugs",value:[s.appealed||0,s.applications||0,s.reports||0,s.bugs||0].join(" / ")});
 return i.reply({embeds:[embed],ephemeral:true});
}};

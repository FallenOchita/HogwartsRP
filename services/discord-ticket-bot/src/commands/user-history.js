const {SlashCommandBuilder,PermissionFlagsBits,EmbedBuilder}=require("discord.js");const Ticket=require("../models/Ticket");const {isAdmin}=require("../utils/permissions");
module.exports={data:new SlashCommandBuilder().setName("user-history").setDescription("View a member ticket history").addUserOption(o=>o.setName("user").setDescription("Member to inspect").setRequired(true)).setDefaultMemberPermissions(PermissionFlagsBits.ModerateMembers),async execute(i){
 if(!isAdmin(i.member))return i.reply({content:"Administrator access required.",ephemeral:true});const user=i.options.getUser("user");
 const rows=await Ticket.find({guildId:i.guildId,userId:user.id}).sort({createdAt:-1}).limit(15).lean();
 const description=rows.length?rows.map(t=>"#"+t.ticketId+" · "+t.ticketType+" · "+t.status+" · <t:"+Math.floor(t.createdAt.getTime()/1000)+":R>").join("\n"):"No tickets found.";
 return i.reply({embeds:[new EmbedBuilder().setTitle("Ticket History — "+user.tag).setColor(0x5b4b8a).setDescription(description)],ephemeral:true});
}};

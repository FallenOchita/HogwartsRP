const {SlashCommandBuilder,EmbedBuilder}=require("discord.js");const Ticket=require("../models/Ticket");const {isAdmin}=require("../utils/permissions");
module.exports={data:new SlashCommandBuilder().setName("staff-review-queue").setDescription("View pending staff reviews"),async execute(i){
 if(!isAdmin(i.member))return i.reply({content:"Administrator access required.",ephemeral:true});
 const rows=await Ticket.find({guildId:i.guildId,status:"open",reviewRequestedAt:{$ne:null},reviewedAt:null}).sort({reviewRequestedAt:1}).limit(25).lean();
 const description=rows.length?rows.map(t=>"#"+t.ticketId+" · "+t.ticketType+" · <#"+t.channelId+">").join("\n"):"No pending reviews.";
 return i.reply({embeds:[new EmbedBuilder().setTitle("Staff Review Queue").setColor(0x5b4b8a).setDescription(description)],ephemeral:true});
}};

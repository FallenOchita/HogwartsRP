const {SlashCommandBuilder}=require("discord.js");const Ticket=require("../models/Ticket");const {isAdmin,canManageTickets}=require("../utils/permissions");const config=require("../config/env");const {types}=require("../config/ticketTypes");const {closeTicket}=require("../services/ticketService");
module.exports={data:new SlashCommandBuilder().setName("close-ticket").setDescription("Close the current ticket").addStringOption(o=>o.setName("reason").setDescription("Reason for closing").setMaxLength(500)),async execute(i){
 const t=await Ticket.findOne({channelId:i.channelId,status:"open"});if(!t)return i.reply({content:"This is not an open ticket.",ephemeral:true});
 const role=config.roles[types[t.ticketType]?.department];if(t.userId!==i.user.id&&!isAdmin(i.member)&&!canManageTickets(i.member,role)&&!t.assignedStaff.includes(i.user.id))return i.reply({content:"You cannot close this ticket.",ephemeral:true});
 await i.deferReply({ephemeral:true});await closeTicket({client:i.client,ticket:t,actorId:i.user.id,reason:i.options.getString("reason")||"Closed by command"});return i.editReply("Ticket closed and transcript generated.");
}};

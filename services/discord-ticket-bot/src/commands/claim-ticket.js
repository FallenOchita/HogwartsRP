const {SlashCommandBuilder}=require("discord.js");const Ticket=require("../models/Ticket");const {canManageTickets,isAdmin}=require("../utils/permissions");const config=require("../config/env");const {types}=require("../config/ticketTypes");const manager=require("../managers/ticketManager");
module.exports={data:new SlashCommandBuilder().setName("claim-ticket").setDescription("Claim the current ticket"),async execute(i){
 const t=await Ticket.findOne({channelId:i.channelId,status:"open"});if(!t)return i.reply({content:"This is not an open ticket.",ephemeral:true});
 const role=config.roles[types[t.ticketType]?.department];if((t.ticketType==="report-staff"?!isAdmin(i.member):(!isAdmin(i.member)&&!canManageTickets(i.member,role)))&&!t.assignedStaff.includes(i.user.id))return i.reply({content:"Staff access required.",ephemeral:true});
 await manager.claim(i.client,t,i.user.id);return i.reply({content:"Ticket claimed.",ephemeral:true});
}};

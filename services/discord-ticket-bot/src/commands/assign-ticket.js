const {SlashCommandBuilder}=require("discord.js");const Ticket=require("../models/Ticket");const {isAdmin,canManageTickets}=require("../utils/permissions");const config=require("../config/env");const {types}=require("../config/ticketTypes");const audit=require("../services/auditService");
module.exports={data:new SlashCommandBuilder().setName("assign-ticket").setDescription("Assign or remove a staff member from this ticket")
 .addUserOption(o=>o.setName("member").setDescription("Staff member").setRequired(true))
 .addStringOption(o=>o.setName("action").setDescription("Assignment action").setRequired(true).addChoices({name:"Assign",value:"assign"},{name:"Unassign",value:"unassign"})),
 async execute(i){
  const ticket=await Ticket.findOne({channelId:i.channelId,status:"open"});if(!ticket)return i.reply({content:"This is not an open ticket.",ephemeral:true});
  const type=types[ticket.ticketType],role=config.roles[type.department];
  if(!isAdmin(i.member)&&!canManageTickets(i.member,role))return i.reply({content:"Department staff access required.",ephemeral:true});
  const targetUser=i.options.getUser("member"),target=await i.guild.members.fetch(targetUser.id).catch(()=>null),action=i.options.getString("action");
  if(!target)return i.reply({content:"Member is not in this server.",ephemeral:true});
  if(action==="assign"&&!isAdmin(target)&&!canManageTickets(target,role))return i.reply({content:"The selected member does not have the required department role.",ephemeral:true});
  if(action==="assign"){
   if(!ticket.assignedStaff.includes(target.id))ticket.assignedStaff.push(target.id);
   await i.channel.permissionOverwrites.edit(target.id,{ViewChannel:true,SendMessages:true,ReadMessageHistory:true,AttachFiles:true});
  }else{
   ticket.assignedStaff=ticket.assignedStaff.filter(id=>id!==target.id);
   await i.channel.permissionOverwrites.delete(target.id).catch(()=>{});
  }
  await ticket.save();await audit.record(i.client,{guildId:ticket.guildId,ticketId:ticket.ticketId,channelId:ticket.channelId,actorId:i.user.id,action:action==="assign"?"TICKET_ASSIGNED":"TICKET_UNASSIGNED",details:{targetId:target.id}}).catch(()=>{});
  return i.reply({content:(action==="assign"?"Assigned ":"Unassigned ")+target.toString()+".",ephemeral:false});
 }};

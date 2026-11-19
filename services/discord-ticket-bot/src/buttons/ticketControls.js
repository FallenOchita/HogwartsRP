const Ticket=require("../models/Ticket");const config=require("../config/env");const {types}=require("../config/ticketTypes");
const {canManageTickets,isAdmin}=require("../utils/permissions");const manager=require("../managers/ticketManager");const audit=require("../services/auditService");const {closeTicket}=require("../services/ticketService");
async function execute(i){
 const action=i.customId.slice("ticket:".length),ticket=await Ticket.findOne({channelId:i.channelId,status:"open"});
 if(!ticket)return i.reply({content:"This ticket is no longer open.",ephemeral:true});
 const type=types[ticket.ticketType],role=config.roles[type?.department];
 if(action==="close"){
  if(ticket.userId!==i.user.id&&!isAdmin(i.member)&&!canManageTickets(i.member,role))return i.reply({content:"You cannot close this ticket.",ephemeral:true});
  await i.deferReply({ephemeral:true});await closeTicket({client:i.client,ticket,actorId:i.user.id,reason:"Closed using ticket controls"});return i.editReply("Ticket closed.");
 }
 if(!canManageTickets(i.member,role))return i.reply({content:"Staff access required.",ephemeral:true});
 if(action==="claim"){await manager.claim(i.client,ticket,i.user.id);return i.reply({content:"Ticket claimed.",ephemeral:true});}
 if(action==="unclaim"){await manager.unclaim(i.client,ticket,i.user.id,i.member);return i.reply({content:"Ticket unclaimed.",ephemeral:true});}
 if(action==="review"){ticket.reviewedAt=new Date();ticket.reviewedBy=i.user.id;await ticket.save();await audit.record(i.client,{guildId:ticket.guildId,ticketId:ticket.ticketId,channelId:ticket.channelId,actorId:i.user.id,action:"REVIEW_REQUESTED"});return i.reply({content:"Review request recorded.",ephemeral:true});}
 if(action==="escalate"){ticket.escalated=true;ticket.priority="Critical";await ticket.save();await audit.record(i.client,{guildId:ticket.guildId,ticketId:ticket.ticketId,channelId:ticket.channelId,actorId:i.user.id,action:"TICKET_ESCALATED"});return i.reply({content:"Ticket escalated to Critical.",ephemeral:true});}
 if(["approve","deny","revision"].includes(action)){
  if(!["character-application","whitelist-application","character-appeal"].includes(ticket.ticketType))return i.reply({content:"This workflow is not available for this ticket type.",ephemeral:true});
  await audit.record(i.client,{guildId:ticket.guildId,ticketId:ticket.ticketId,channelId:ticket.channelId,actorId:i.user.id,action:"APPLICATION_"+action.toUpperCase()});
  return i.reply({content:"Application decision recorded: "+action+".",ephemeral:false});
 }
}
module.exports={execute};

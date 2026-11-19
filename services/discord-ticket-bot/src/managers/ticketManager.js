const {ChannelType,PermissionFlagsBits}=require("discord.js");
const Ticket=require("../models/Ticket");const config=require("../config/env");const {types}=require("../config/ticketTypes");const {ticketNumber,slug}=require("../utils/ids");
const audit=require("../services/auditService");const stats=require("../services/statisticsService");const userState=require("../services/userTicketStateService");
const roleFor=type=>config.roles[type.department]||null;const categoryFor=type=>config.categories[type.group]||null;
async function createTicket({client,guild,user,typeId,answers}){
 const type=types[typeId];if(!type)throw new Error("Unknown ticket type");
 const duplicate=await Ticket.findOne({guildId:guild.id,userId:user.id,ticketType:typeId,status:"open"});if(duplicate)throw new Error("You already have an open ticket: <#"+duplicate.channelId+">");
 if(typeId==="report-staff"){
  if(!config.adminRoleIds.length||!config.auditChannelId)throw new Error("Staff reports require ADMIN_ROLE_IDS and AUDIT_CHANNEL to be configured.");
  for(const id of config.adminRoleIds)if(!guild.roles.cache.has(id))throw new Error("An ADMIN_ROLE_IDS entry does not exist in this server.");
 }
 let channel=null,ticket=null;
 const ticketId=ticketNumber(),name=(type.group==="APPEALS"?"appeal":type.group==="REPORTS"?"report":"ticket")+"-"+slug(user.username)+"-"+ticketId;
 const overwrites=[
 {id:guild.roles.everyone.id,deny:[PermissionFlagsBits.ViewChannel]},
 {id:user.id,allow:[PermissionFlagsBits.ViewChannel,PermissionFlagsBits.SendMessages,PermissionFlagsBits.ReadMessageHistory,PermissionFlagsBits.AttachFiles,PermissionFlagsBits.EmbedLinks]},
 {id:client.user.id,allow:[PermissionFlagsBits.ViewChannel,PermissionFlagsBits.SendMessages,PermissionFlagsBits.ReadMessageHistory,PermissionFlagsBits.ManageChannels,PermissionFlagsBits.AttachFiles]}
 ];
 const configuredStaffRole=typeId==="report-staff"?null:roleFor(type);if(configuredStaffRole&&!guild.roles.cache.has(configuredStaffRole))throw new Error("Configured staff role for "+type.department+" was not found in this server.");const staffRole=configuredStaffRole;if(staffRole)overwrites.push({id:staffRole,allow:[PermissionFlagsBits.ViewChannel,PermissionFlagsBits.SendMessages,PermissionFlagsBits.ReadMessageHistory,PermissionFlagsBits.AttachFiles]});
 if(typeId==="report-staff")for(const adminRoleId of config.adminRoleIds)if(guild.roles.cache.has(adminRoleId))overwrites.push({id:adminRoleId,allow:[PermissionFlagsBits.ViewChannel,PermissionFlagsBits.SendMessages,PermissionFlagsBits.ReadMessageHistory,PermissionFlagsBits.AttachFiles]});
 await userState.reserve(guild.id,user.id);
 try{
  channel=await guild.channels.create({name,type:ChannelType.GuildText,parent:categoryFor(type),topic:"HogwartsRP ticket "+ticketId+" | "+type.label+" | opener "+user.id,permissionOverwrites:overwrites,reason:"Ticket opened by "+user.tag});
  ticket=await Ticket.create({ticketId,ticketType:typeId,userId:user.id,channelId:channel.id,guildId:guild.id,priority:type.priority,answers});
  const staffMentions=typeId==="report-staff"?config.adminRoleIds.map(id=>"<@&"+id+">").join(" "):(staffRole?"<@&"+staffRole+">":"");
  await channel.send({content:(staffMentions?staffMentions+" ":"")+"<@"+user.id+">",embeds:[require("../embeds/ticketEmbed").opened(ticket,type)],components:require("../embeds/ticketEmbed").controls(typeId)});
  const increments=[["opened",1]];
  if(type.group==="APPEALS")increments.push(["appealed",1]);
  if(type.group==="APPLICATIONS")increments.push(["applications",1]);
  if(type.group==="REPORTS")increments.push(["reports",1]);
  if(typeId==="bug-report")increments.push(["bugs",1]);
  await Promise.allSettled(increments.map(([key,value])=>stats.increment(guild.id,key,value)));
  await audit.record(client,{guildId:guild.id,ticketId,channelId:channel.id,actorId:user.id,action:"TICKET_CREATED",details:{typeId,priority:type.priority}}).catch(()=>{});
  return{ticket,channel};
 }catch(error){
  if(ticket)await Ticket.deleteOne({_id:ticket._id}).catch(()=>{});
  if(channel)await channel.delete("Ticket creation rollback").catch(()=>{});
  await userState.release(guild.id,user.id,{resetCooldown:true}).catch(()=>{});
  throw error;
 }
}
async function claim(client,ticket,actorId){
 if(ticket.claimedBy&&ticket.claimedBy!==actorId)throw new Error("Already claimed by <@"+ticket.claimedBy+">.");
 ticket.claimedBy=actorId;if(!ticket.firstResponseAt)ticket.firstResponseAt=new Date();await ticket.save();
 await audit.record(client,{guildId:ticket.guildId,ticketId:ticket.ticketId,channelId:ticket.channelId,actorId,action:"TICKET_CLAIMED"}).catch(()=>{});
}
async function unclaim(client,ticket,actorId,member){
 const permissions=require("../utils/permissions");
 if(ticket.claimedBy&&ticket.claimedBy!==actorId&&!permissions.isAdmin(member))throw new Error("Only the claimer or an administrator can unclaim this ticket.");
 const previous=ticket.claimedBy;ticket.claimedBy=null;await ticket.save();
 await audit.record(client,{guildId:ticket.guildId,ticketId:ticket.ticketId,channelId:ticket.channelId,actorId,action:"TICKET_UNCLAIMED",details:{previous}}).catch(()=>{});
}
async function close(client,ticket,actorId,reason){
 const closedAt=new Date();
 const updated=await Ticket.findOneAndUpdate({_id:ticket._id,status:"open"},{$set:{status:"closed",closedAt,closedBy:actorId,closeReason:reason,transcriptPath:ticket.transcriptPath,transcriptMarkdownPath:ticket.transcriptMarkdownPath}},{new:true});
 if(!updated)throw new Error("This ticket has already been closed.");
 await userState.release(ticket.guildId,ticket.userId);
 await stats.increment(ticket.guildId,"closed").catch(()=>{});
 await audit.record(client,{guildId:ticket.guildId,ticketId:ticket.ticketId,channelId:ticket.channelId,actorId,action:"TICKET_CLOSED",details:{reason}}).catch(()=>{});
 return updated;
}
module.exports={createTicket,claim,unclaim,close,roleFor};

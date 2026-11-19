const {PermissionFlagsBits}=require("discord.js");
const config=require("../config/env");
function hasAnyRole(member,ids=[]){return Boolean(member&&ids.some(id=>member.roles?.cache?.has(id)));}
function isAdmin(member){return Boolean(member?.permissions?.has(PermissionFlagsBits.Administrator)||hasAnyRole(member,config.adminRoleIds));}
function canManageTickets(member,roleId){return isAdmin(member)||hasAnyRole(member,[roleId].filter(Boolean));}
module.exports={hasAnyRole,isAdmin,canManageTickets};

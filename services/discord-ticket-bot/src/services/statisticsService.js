const Stats=require("../models/TicketStatistics");const Ticket=require("../models/Ticket");
async function increment(guildId,key,amount=1){if(!["opened","closed","appealed","applications","reports","bugs"].includes(key))return;await Stats.updateOne({guildId},{$inc:{[key]:amount}},{upsert:true});}
async function get(guildId){
 const [stats,open,aggregate]=await Promise.all([Stats.findOne({guildId}).lean(),Ticket.countDocuments({guildId,status:"open"}),Ticket.aggregate([
 {$match:{guildId,status:"closed",closedAt:{$ne:null}}},
 {$project:{duration:{$subtract:["$closedAt","$createdAt"]},response:{$cond:[{$ne:["$firstResponseAt",null]},{$subtract:["$firstResponseAt","$createdAt"]},null]}}},
 {$group:{_id:null,avgResolution:{$avg:"$duration"},avgResponse:{$avg:"$response"}}}
 ])]);
 const a=aggregate[0]||{};return{...(stats||{}),open,closed:stats?.closed||0,avgResponseMs:a.avgResponse||0,avgResolutionMs:a.avgResolution||0};
}
module.exports={increment,get};

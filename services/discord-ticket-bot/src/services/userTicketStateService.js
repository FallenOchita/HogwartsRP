const State=require("../models/UserTicketState");const Ticket=require("../models/Ticket");const config=require("../config/env");
async function reserve(guildId,userId){
 try{await State.updateOne({guildId,userId},{$setOnInsert:{guildId,userId,openCount:0,cooldownUntil:new Date(0)}},{upsert:true});}
 catch(error){if(error.code!==11000)throw error;}
 const now=new Date(),reserved=await State.findOneAndUpdate({guildId,userId,openCount:{$lt:config.maxOpenTickets},cooldownUntil:{$lte:now}},{$inc:{openCount:1},$set:{cooldownUntil:new Date(now.getTime()+config.cooldownMs)}},{new:true});
 if(reserved)return reserved;
 const state=await State.findOne({guildId,userId}).lean();
 if((state?.openCount||0)>=config.maxOpenTickets)throw new Error("You may have at most "+config.maxOpenTickets+" open tickets.");
 const minutes=Math.max(1,Math.ceil(((state?.cooldownUntil?.getTime()||0)-Date.now())/60000));
 throw new Error("Please wait "+minutes+" minute(s) before opening another ticket.");
}
async function release(guildId,userId,{resetCooldown=false}={}){
 await State.updateOne({guildId,userId},{$inc:{openCount:-1},...(resetCooldown?{$set:{cooldownUntil:new Date(0)}}:{})});
 await State.updateMany({guildId,userId,openCount:{$lt:0}},{$set:{openCount:0}});
}
async function reconcile(){
 const counts=await Ticket.aggregate([{$match:{status:"open"}},{$group:{_id:{guildId:"$guildId",userId:"$userId"},count:{$sum:1}}}]);
 await State.updateMany({},{$set:{openCount:0}});
 if(counts.length)await State.bulkWrite(counts.map(x=>({updateOne:{filter:{guildId:x._id.guildId,userId:x._id.userId},update:{$set:{openCount:x.count}},upsert:true}})));
}
module.exports={reserve,release,reconcile};

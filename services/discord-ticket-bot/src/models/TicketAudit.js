const mongoose=require("mongoose");
const schema=new mongoose.Schema({guildId:{type:String,required:true,index:true},ticketId:{type:String,required:true,index:true},channelId:String,actorId:String,action:{type:String,required:true},details:{type:mongoose.Schema.Types.Mixed,default:{}}},{timestamps:true});
module.exports=mongoose.model("TicketAudit",schema);

const mongoose=require("mongoose");
const schema=new mongoose.Schema({guildId:{type:String,required:true},userId:{type:String,required:true},openCount:{type:Number,default:0,min:0},cooldownUntil:{type:Date,default:new Date(0)}},{timestamps:true});
schema.index({guildId:1,userId:1},{unique:true});
module.exports=mongoose.model("UserTicketState",schema);

const mongoose=require("mongoose");
const schema=new mongoose.Schema({guildId:{type:String,required:true,unique:true},opened:{type:Number,default:0},closed:{type:Number,default:0},appealed:{type:Number,default:0},applications:{type:Number,default:0},reports:{type:Number,default:0},bugs:{type:Number,default:0}},{timestamps:true});
module.exports=mongoose.model("TicketStatistics",schema);

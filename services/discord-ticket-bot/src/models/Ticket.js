const mongoose=require("mongoose");
const schema=new mongoose.Schema({
 ticketId:{type:String,required:true,unique:true,index:true},ticketType:{type:String,required:true,index:true},
 userId:{type:String,required:true,index:true},channelId:{type:String,required:true,unique:true},guildId:{type:String,required:true,index:true},
 status:{type:String,enum:["open","closed"],default:"open",index:true},claimedBy:{type:String,default:null},assignedStaff:{type:[String],default:[]},
 priority:{type:String,enum:["Low","Medium","High","Critical"],default:"Medium"},answers:{type:Map,of:String,default:{}},
 firstResponseAt:{type:Date,default:null},closedAt:{type:Date,default:null},closedBy:{type:String,default:null},closeReason:{type:String,default:null},
 transcriptPath:{type:String,default:null},transcriptMarkdownPath:{type:String,default:null},escalated:{type:Boolean,default:false},
 reviewRequestedAt:{type:Date,default:null},reviewedAt:{type:Date,default:null},reviewedBy:{type:String,default:null}
},{timestamps:true,minimize:false});
schema.index({guildId:1,userId:1,status:1});schema.index({guildId:1,status:1,createdAt:-1});
schema.index({guildId:1,userId:1,ticketType:1},{unique:true,partialFilterExpression:{status:"open"}});
module.exports=mongoose.model("Ticket",schema);

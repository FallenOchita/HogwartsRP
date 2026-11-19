const mongoose=require("mongoose");const logger=require("../utils/logger");
module.exports=async function connect(uri){
 mongoose.set("strictQuery",true);
 await mongoose.connect(uri,{serverSelectionTimeoutMS:10000,maxPoolSize:20,minPoolSize:2,autoIndex:false});
 const models=[require("../models/Ticket"),require("../models/TicketStatistics"),require("../models/TicketAudit"),require("../models/UserTicketState")];
 await Promise.all(models.map(model=>model.createIndexes()));
 logger.info("MongoDB connected and indexes verified");
};

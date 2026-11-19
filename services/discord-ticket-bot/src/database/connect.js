const mongoose=require("mongoose");const logger=require("../utils/logger");
module.exports=async function connect(uri){mongoose.set("strictQuery",true);await mongoose.connect(uri,{serverSelectionTimeoutMS:10000,maxPoolSize:20,minPoolSize:2,autoIndex:process.env.NODE_ENV!=="production"});logger.info("MongoDB connected");};

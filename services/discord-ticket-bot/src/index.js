require("dotenv").config();const {Client,Collection,GatewayIntentBits}=require("discord.js");const fs=require("node:fs");const path=require("node:path");
const config=require("./config/env");const connect=require("./database/connect");const logger=require("./utils/logger");
const client=new Client({intents:[GatewayIntentBits.Guilds]});client.commands=new Collection();
for(const file of fs.readdirSync(path.join(__dirname,"commands")).filter(f=>f.endsWith(".js"))){const command=require(path.join(__dirname,"commands",file));client.commands.set(command.data.name,command);}
for(const file of fs.readdirSync(path.join(__dirname,"events")).filter(f=>f.endsWith(".js"))){const event=require(path.join(__dirname,"events",file));client[event.once?"once":"on"](event.name,(...args)=>event.execute(...args));}
let stopping=false;async function shutdown(signal){if(stopping)return;stopping=true;logger.info("Shutting down",{signal});client.destroy();await require("mongoose").disconnect().catch(()=>{});process.exit(0);}
process.on("SIGTERM",()=>shutdown("SIGTERM"));process.on("SIGINT",()=>shutdown("SIGINT"));
process.on("unhandledRejection",error=>logger.error("Unhandled rejection",{message:error?.message,stack:error?.stack}));
process.on("uncaughtException",error=>{logger.error("Uncaught exception",{message:error.message,stack:error.stack});shutdown("uncaughtException");});
(async()=>{await connect(config.mongoUri);await client.login(config.token);})().catch(error=>{logger.error("Startup failed",{message:error.message,stack:error.stack});process.exit(1);});

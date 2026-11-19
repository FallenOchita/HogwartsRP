const crypto=require("node:crypto");
function ticketNumber(){return crypto.randomBytes(6).toString("hex").toUpperCase();}
function slug(value){return value.normalize("NFKD").toLowerCase().replace(/[^a-z0-9]+/g,"-").replace(/^-|-$/g,"").slice(0,18)||"user";}
module.exports={ticketNumber,slug};

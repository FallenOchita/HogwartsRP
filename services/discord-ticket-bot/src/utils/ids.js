const crypto=require("node:crypto");
function ticketNumber(){return String(crypto.randomInt(100000,999999));}
function slug(value){return value.normalize("NFKD").toLowerCase().replace(/[^a-z0-9]+/g,"-").replace(/^-|-$/g,"").slice(0,18)||"user";}
module.exports={ticketNumber,slug};

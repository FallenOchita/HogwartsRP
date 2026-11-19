const fs=require("node:fs/promises");const path=require("node:path");const config=require("../config/env");
const escapeHtml=s=>String(s??"").replace(/[&<>"']/g,c=>({"&":"&amp;","<":"&lt;",">":"&gt;",'"':"&quot;","'":"&#39;"}[c]));
async function fetchMessages(channel){
 const all=[];let before;
 while(all.length<5000){const batch=await channel.messages.fetch({limit:100,...(before?{before}:{})});if(!batch.size)break;all.push(...batch.values());before=batch.last().id;if(batch.size<100)break;}
 return all.sort((a,b)=>a.createdTimestamp-b.createdTimestamp);
}
async function generate(channel,ticket){
 await fs.mkdir(config.transcriptDir,{recursive:true});const messages=await fetchMessages(channel);
 const rows=messages.map(m=>{const content=[m.content,...m.attachments.map(a=>a.url)].filter(Boolean).join("\n");return{author:m.author?.tag||"Unknown",time:new Date(m.createdTimestamp).toISOString(),content,html:escapeHtml(content).replace(/\n/g,"<br>")};});
 const html='<!doctype html><html><head><meta charset="utf-8"><title>Ticket '+ticket.ticketId+'</title><style>body{font:14px system-ui;background:#171717;color:#eee;margin:2rem}article{border-bottom:1px solid #444;padding:1rem 0}.meta{color:#aaa;font-size:12px}pre{white-space:pre-wrap;overflow-wrap:anywhere;font:inherit}</style></head><body><h1>HogwartsRP Ticket #'+ticket.ticketId+'</h1>'+rows.map(r=>'<article><div class="meta">'+escapeHtml(r.author)+' · '+escapeHtml(r.time)+'</div><pre>'+r.html+'</pre></article>').join("")+'</body></html>';
 const markdown="# HogwartsRP Ticket #"+ticket.ticketId+"\n\n"+rows.map(r=>"## "+r.author+" — "+r.time+"\n\n"+r.content+"\n").join("\n");
 const htmlPath=path.join(config.transcriptDir,"ticket-"+ticket.ticketId+".html"),mdPath=path.join(config.transcriptDir,"ticket-"+ticket.ticketId+".md");
 await Promise.all([fs.writeFile(htmlPath,html,{mode:0o600}),fs.writeFile(mdPath,markdown,{mode:0o600})]);
 return{htmlPath,mdPath,messages:rows.length};
}
module.exports={generate};

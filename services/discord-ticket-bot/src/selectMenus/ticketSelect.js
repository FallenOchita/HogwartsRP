const {types,fields,defaults}=require("../config/ticketTypes");
const {ModalBuilder,ActionRowBuilder,TextInputBuilder,TextInputStyle}=require("discord.js");
function buildModal(typeId){
 const type=types[typeId];if(!type)throw new Error("Unknown ticket type");
 const schema=fields[typeId]||defaults,modal=new ModalBuilder().setCustomId("ticket:create:"+typeId).setTitle(("New: "+type.label).slice(0,45));
 for(const [id,label,style] of schema.slice(0,5)){
  const input=new TextInputBuilder().setCustomId(id).setLabel(label.slice(0,45)).setStyle(style==="PARAGRAPH"?TextInputStyle.Paragraph:TextInputStyle.Short)
   .setRequired(!["additional_information","evidence"].includes(id)).setMaxLength(style==="PARAGRAPH"?1500:100);
  modal.addComponents(new ActionRowBuilder().addComponents(input));
 }
 return modal;
}
module.exports={buildModal};

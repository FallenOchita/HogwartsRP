const test=require("node:test");const assert=require("node:assert/strict");
const {types,fields}=require("../src/config/ticketTypes");const {slug}=require("../src/utils/ids");
test("all requested ticket categories are registered",()=>{assert.equal(Object.keys(types).length,21);});
test("ticket type IDs are unique and include department routing",()=>{for(const [id,type] of Object.entries(types)){assert.equal(type.id,id);assert.ok(type.group);assert.ok(type.department);assert.ok(["Low","Medium","High","Critical"].includes(type.priority));}});
test("custom modal definitions never exceed Discord's five text inputs",()=>{for(const schema of Object.values(fields))assert.ok(schema.length<=5);});
test("channel slug is safe and bounded",()=>{const value=slug("Jane Doe / Hogwarts RP");assert.match(value,/^[a-z0-9-]+$/);assert.ok(value.length<=18);});

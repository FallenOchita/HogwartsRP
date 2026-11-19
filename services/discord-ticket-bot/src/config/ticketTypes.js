const definitions=[
["general-support","General Support","SUPPORT","SUPPORT","Medium"],["new-player-help","New Player Help","SUPPORT","SUPPORT","Medium"],["server-questions","Server Questions","SUPPORT","SUPPORT","Low"],
["character-application","Character Application","APPLICATIONS","APPLICATION","High"],["character-rename","Character Rename Request","APPLICATIONS","APPLICATION","Low"],["character-recovery","Character Recovery","APPLICATIONS","APPLICATION","High"],["character-transfer","Character Transfer","APPLICATIONS","APPLICATION","Medium"],
["report-player","Report Player","REPORTS","MODERATION","High"],["report-staff","Report Staff Member","REPORTS","MODERATION","Critical"],["admin-review","Admin Review Request","REPORTS","MODERATION","High"],
["ban-appeal","Ban Appeal","APPEALS","MODERATION","High"],["character-appeal","Character Appeal","APPEALS","APPLICATION","Medium"],["warning-appeal","Warning Appeal","APPEALS","MODERATION","Medium"],
["bug-report","Bug Report","TECHNICAL","SUPPORT","Medium"],["connection-problems","Connection Problems","TECHNICAL","SUPPORT","High"],["mod-installation","Mod Installation Help","TECHNICAL","SUPPORT","Medium"],
["feature-suggestion","Feature Suggestion","DEVELOPMENT","DEVELOPMENT","Low"],["lua-adapter-feedback","Lua Adapter Feedback","DEVELOPMENT","DEVELOPMENT","Medium"],["multiplayer-testing","Multiplayer Testing Report","DEVELOPMENT","DEVELOPMENT","High"],["development-discussion","Development Discussion","DEVELOPMENT","DEVELOPMENT","Low"],
["whitelist-application","Whitelist Application","APPLICATIONS","APPLICATION","High"]
];
const types=Object.fromEntries(definitions.map(([id,label,group,department,priority])=>[id,{id,label,group,department,priority,description:label+" for HogwartsRP"}]));
const fields={
"ban-appeal":[["discord_username","Discord Username","SHORT"],["ban_reason","Ban Reason","PARAGRAPH"],["appeal_explanation","Appeal Explanation","PARAGRAPH"],["additional_information","Additional Information","PARAGRAPH"]],
"character-application":[["character_name","Character Name","SHORT"],["character_age","Character Age","SHORT"],["house","House","SHORT"],["background","Character Background","PARAGRAPH"],["roleplay_experience","Roleplay Experience","PARAGRAPH"]],
"bug-report":[["bug_summary","Bug Summary","SHORT"],["reproduction_steps","Reproduction Steps","PARAGRAPH"],["expected_result","Expected Result","PARAGRAPH"],["actual_result","Actual Result","PARAGRAPH"]],
"whitelist-application":[["applicant","Applicant / Discord Username","SHORT"],["age","Age bracket (if required)","SHORT"],["timezone","Timezone","SHORT"],["experience","Roleplay Experience","PARAGRAPH"],["character_concept","Character Concept","PARAGRAPH"]],
"report-player":[["reported_user","Reported User","SHORT"],["incident","What happened?","PARAGRAPH"],["evidence","Evidence links (optional)","PARAGRAPH"]],
"report-staff":[["reported_staff","Staff Member","SHORT"],["incident","What happened?","PARAGRAPH"],["evidence","Evidence links (optional)","PARAGRAPH"]],
"feature-suggestion":[["suggestion","Feature Suggestion","PARAGRAPH"],["benefit","How would this help?","PARAGRAPH"]],
"lua-adapter-feedback":[["adapter_area","Adapter / Feature","SHORT"],["feedback","Feedback","PARAGRAPH"]],
"multiplayer-testing":[["test_area","What did you test?","SHORT"],["observations","Results and observations","PARAGRAPH"],["reproduction","Steps to reproduce","PARAGRAPH"]]
};
const defaults=[["summary","How can we help?","PARAGRAPH"],["details","Please provide relevant details.","PARAGRAPH"]];
module.exports={types,fields,defaults};

from __future__ import annotations
import argparse, hashlib, html, json, logging, os, re
from collections import defaultdict
from datetime import datetime, timezone
from pathlib import Path

LOG = logging.getLogger("asset_research")
CONTAINERS = {".pak", ".utoc", ".ucas"}
UNREAL = {".uasset", ".umap"}
TEXT = {".ini", ".json", ".xml", ".txt", ".csv"}
CATEGORIES = {
 "Blueprints": ("blueprint", "bp_"), "Characters": ("character", "student", "teacher", "npc", "vendor", "creature", "enemy", "companion"),
 "Animations": ("anim", "montage", "blendspace", "sequence"), "Textures": ("texture", "tex_", "t_"),
 "Materials": ("material", "mi_", "m_"), "Widgets": ("widget", "hud", "menu"),
 "Audio": ("audio", "sound", "music", "dialog", "voice", "sfx", "amb"),
 "Particles": ("particle", "niagara", "effect", "vfx"), "Worlds": ("world", "map", "level", "persistent", "sublevel"),
 "DataTables": ("datatable", "data_table", "dt_"), "Enums": ("enum",), "Structs": ("struct",), "Interfaces": ("interface", "bpi_")
}

def sha256(path):
 h=hashlib.sha256()
 with path.open("rb") as f:
  for block in iter(lambda:f.read(1024*1024), b""): h.update(block)
 return h.hexdigest()

def classify(path):
 text=(path.stem+" "+path.as_posix()).lower()
 found=[name for name,tokens in CATEGORIES.items() if any(t in text for t in tokens)]
 if path.suffix.lower()==".umap" and "Worlds" not in found: found.append("Worlds")
 return found

def scan(root, hash_files=True, max_file_mb=None, include_hidden=False):
 root=root.expanduser().resolve(strict=True)
 if not root.is_dir(): raise ValueError("Game root must be a directory")
 rows=[]
 for current,dirs,names in os.walk(root,followlinks=False):
  dirs[:]=sorted(dirs)
  for name in sorted(names):
   p=Path(current,name)
   try:
    if p.is_symlink() or not p.is_file(): continue
    st=p.stat(); ext=p.suffix.lower()
    skip=max_file_mb is not None and st.st_size>max_file_mb*1024*1024
    rows.append({"file_name":p.name,"file_size_bytes":st.st_size,"file_type":ext or "[no extension]",
     "relative_path":p.relative_to(root).as_posix(),"last_modified_utc":datetime.fromtimestamp(st.st_mtime,timezone.utc).isoformat(),
     "sha256":None if (not hash_files or skip) else sha256(p),"hash_status":"DISABLED" if not hash_files else ("SKIPPED_SIZE_LIMIT" if skip else "COMPUTED"),
     "is_container":ext in CONTAINERS,"is_unreal_asset":ext in UNREAL,"asset_categories":classify(p) if ext in UNREAL else []})
   except OSError as e: LOG.warning("Skipped %s: %s",p,e)
 LOG.info("Inventoried %d files",len(rows)); return rows

def asset_record(root,row,max_asset_mb):
 p=root/row["relative_path"]
 rec={"asset_path":row["relative_path"],"object_path":f"/{p.stem}.{p.stem}","package":p.stem,"class":"UNKNOWN",
      "dependencies":[],"references":[],"metadata_status":"OPAQUE_BINARY",
      "metadata_note":"String heuristic only; no Unreal package parser is bundled."}
 if p.stat().st_size>max_asset_mb*1024*1024:
  rec["metadata_status"]="SKIPPED_SIZE_LIMIT"; return rec
 try:
  if p.suffix.lower() in TEXT: data=p.read_text(encoding="utf-8-sig",errors="replace")[:200000]
  else:
   raw=p.read_bytes()
   data="\n".join(x.decode("ascii","replace") for x in re.findall(rb"[ -~]{5,}",raw))
   rec["metadata_status"]="STRING_HEURISTIC"
  if p.suffix.lower() in TEXT: rec["metadata_status"]="TEXT_READ"
  refs=sorted(set(re.findall(r"/Game/[A-Za-z0-9_./-]+",data)))
  rec["references"]=refs[:2000]; rec["dependencies"]=rec["references"]
  rec["candidate_string_count"]=len(data)
 except OSError as e:
  rec["metadata_status"]="READ_ERROR"; rec["metadata_note"]=str(e)
 return rec

def write_json(path,data):
 path.parent.mkdir(parents=True,exist_ok=True); path.write_text(json.dumps(data,indent=2,ensure_ascii=False),encoding="utf-8")

def table_md(title,rows,cols):
 out=[f"# {title}","","Records: "+str(len(rows)),"","| "+" | ".join(cols)+" |","| "+" | ".join("---" for _ in cols)+" |"]
 for r in rows[:10000]: out.append("| "+" | ".join(str(r.get(c,"")).replace("|","\\|").replace("\n"," ") for c in cols)+" |")
 return "\n".join(out)+"\n"

def table_html(title,rows,cols):
 th="".join("<th>"+html.escape(c)+"</th>" for c in cols)
 body="".join("<tr>"+"".join("<td>"+html.escape(str(r.get(c,"")))+"</td>" for c in cols)+"</tr>" for r in rows[:10000])
 return "<!doctype html><meta charset='utf-8'><title>"+html.escape(title)+"</title><style>body{font:14px system-ui;margin:2rem}table{border-collapse:collapse;width:100%}td,th{border:1px solid #ccc;padding:.4rem;overflow-wrap:anywhere}</style><h1>"+html.escape(title)+"</h1><p>"+str(len(rows))+" records</p><table><tr>"+th+"</tr>"+body+"</table>"

def catalog_assets(root,files,max_asset_mb):
 assets=[asset_record(root,r,max_asset_mb) for r in files if r["is_unreal_asset"]]
 categories=defaultdict(list)
 for a in assets:
  for c in classify(Path(a["asset_path"])): categories[c].append(a)
 return {"asset_count":len(assets),"assets":assets,"categories":dict(categories)}

def reports(out,root,files,catalog):
 out.mkdir(parents=True,exist_ok=True)
 write_json(out/"FileInventory.json",{"root":str(root),"file_count":len(files),"files":files})
 write_json(out/"AssetCatalog.json",catalog)
 nodes=[{"id":a["asset_path"],"object_path":a["object_path"],"class":a["class"]} for a in catalog["assets"]]
 edges=[{"source":a["asset_path"],"target":ref,"relationship":"STRING_REFERENCE_CANDIDATE","verified":False} for a in catalog["assets"] for ref in a["references"]]
 write_json(out/"DependencyGraph.json",{"nodes":nodes,"edges":edges,"note":"String-derived candidates, not authoritative Unreal dependency data."})
 specs={"BlueprintInventory":"Blueprints","CharacterCatalog":"Characters","AnimationCatalog":"Animations","DataTableCatalog":"DataTables","EnumCatalog":"Enums","StructCatalog":"Structs","WorldReport":"Worlds","MapInventory":"Worlds","LevelInventory":"Worlds"}
 for name,cat in specs.items():
  rows=catalog["categories"].get(cat,[])
  write_json(out/(name+".json"),{"count":len(rows),"items":rows,"status":"CANDIDATES" if rows else "NO_MATCHING_LOOSE_ASSETS"})
  (out/(name+".md")).write_text(table_md(name,rows,["asset_path","object_path","class","metadata_status"]),encoding="utf-8")
 audio=[f for f in files if f["file_type"] in {".wem",".bnk",".wav",".mp3",".ogg"}]
 write_json(out/"AudioCatalog.json",{"count":len(audio),"files":audio,"asset_candidates":catalog["categories"].get("Audio",[])})
 loc=[f for f in files if f["file_type"]==".locres" or any(x in f["relative_path"].lower() for x in ("localization","subtitle","dialogue"))]
 write_json(out/"LocalizationCatalog.json",{"count":len(loc),"resources":loc,"note":"Resource inventory only; locres payloads are not decoded."})
 cols=["file_name","file_size_bytes","file_type","relative_path","last_modified_utc","sha256"]
 (out/"FileInventory.md").write_text(table_md("File Inventory",files,cols),encoding="utf-8")
 (out/"FileInventory.html").write_text(table_html("File Inventory",files,cols),encoding="utf-8")
 (out/"AssetCatalog.html").write_text(table_html("Asset Catalog",catalog["assets"],["asset_path","object_path","class","metadata_status"]),encoding="utf-8")
 (out/"BlueprintInventory.md").write_text(table_md("Blueprint Inventory",catalog["categories"].get("Blueprints",[]),["asset_path","object_path","class","metadata_status"]),encoding="utf-8")
 write_json(out/"RunSummary.json",{"root":str(root),"file_count":len(files),"asset_count":catalog["asset_count"],"container_files":sum(f["is_container"] for f in files),
 "limitations":["Containers are inventoried but not extracted or decrypted.","Unreal asset metadata is heuristic without a compatible UE4.27 package parser.","Blueprint variables, functions, interfaces, components and verified dependencies are not claimed."]})

def search(out,query,category=None,limit=100):
 data=json.loads((out/"AssetCatalog.json").read_text(encoding="utf-8")); q=query.casefold(); result=[]
 for a in data["assets"]:
  hay=" ".join([a["asset_path"],a.get("object_path") or "",a.get("class","")," ".join(a["references"])]).casefold()
  if q in hay and (not category or category in classify(Path(a["asset_path"]))): result.append(a)
 return result[:max(1,limit)]

def main():
 p=argparse.ArgumentParser(description="Read-only Hogwarts Legacy file and loose-asset catalog")
 sub=p.add_subparsers(dest="cmd",required=True)
 s=sub.add_parser("scan"); s.add_argument("--game-root",required=True,type=Path); s.add_argument("--output",type=Path,default=Path("reports")); s.add_argument("--logs",type=Path,default=Path("logs")); s.add_argument("--no-hash",action="store_true"); s.add_argument("--max-file-mb",type=int); s.add_argument("--max-asset-mb",type=int,default=64); s.add_argument("--include-hidden",action="store_true")
 q=sub.add_parser("search"); q.add_argument("query"); q.add_argument("--output",type=Path,default=Path("reports")); q.add_argument("--category"); q.add_argument("--limit",type=int,default=100)
 a=p.parse_args()
 if a.cmd=="scan":
  a.logs.mkdir(parents=True,exist_ok=True); logging.basicConfig(level=logging.INFO,format="%(asctime)s %(levelname)s %(message)s",handlers=[logging.StreamHandler(),logging.FileHandler(a.logs/"asset-research.log",encoding="utf-8")])
  root=a.game_root.resolve(strict=True); out=a.output.resolve()
  if out==root or root in out.parents: raise ValueError("Report output must be outside the game installation directory.")
  files=scan(root,not a.no_hash,a.max_file_mb,a.include_hidden); cat=catalog_assets(root,files,a.max_asset_mb); reports(out,root,files,cat)
  LOG.info("Completed: %d files, %d loose Unreal assets; reports: %s",len(files),cat["asset_count"],out)
 else: print(json.dumps(search(a.output,a.query,a.category,a.limit),indent=2,ensure_ascii=False))
if __name__=="__main__":
 try: main()
 except Exception: logging.exception("Asset research failed"); raise SystemExit(2)

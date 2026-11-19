import hashlib
import json
import tempfile
import unittest
from pathlib import Path
import sys
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import asset_research as toolkit

class ToolkitTests(unittest.TestCase):
 def test_inventory_hash_and_paths(self):
  with tempfile.TemporaryDirectory() as tmp:
   root=Path(tmp)/"game"; root.mkdir()
   sample=root/"config.ini"; sample.write_text("[x]\na=1",encoding="utf-8")
   rows=toolkit.scan(root)
   self.assertEqual(rows[0]["relative_path"],"config.ini")
   self.assertEqual(rows[0]["sha256"],hashlib.sha256(sample.read_bytes()).hexdigest())
 def test_classification(self):
  self.assertIn("Blueprints",toolkit.classify(Path("Content/BP_Biped_Player.uasset")))
  self.assertIn("Worlds",toolkit.classify(Path("Content/Root.umap")))
 def test_reports_and_search(self):
  with tempfile.TemporaryDirectory() as tmp:
   root=Path(tmp)/"game"; out=Path(tmp)/"reports"; root.mkdir()
   (root/"BP_Student.uasset").write_bytes(b"\x00Student /Game/Characters/BP_Student.BP_Student\x00")
   rows=toolkit.scan(root,hash_files=False); catalog=toolkit.catalog_assets(root,rows,64)
   toolkit.reports(out,root,rows,catalog)
   self.assertTrue((out/"FileInventory.html").exists())
   self.assertTrue((out/"DependencyGraph.json").exists())
   self.assertEqual(len(toolkit.search(out,"Student")),1)
if __name__=="__main__": unittest.main()

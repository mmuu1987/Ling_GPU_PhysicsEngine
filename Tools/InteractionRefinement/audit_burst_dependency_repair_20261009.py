from pathlib import Path
import json,zipfile,hashlib,sys
sys.stdout.reconfigure(encoding='utf-8')
R=Path(__file__).resolve().parents[2];W=R/'outputs/BurstDependencyRepair-20261009-01';P=R/'outputs/B9'
m=json.loads((W/'repair-manifest.json').read_text(encoding='utf-8'))
changed=[];count=0;unexpected=[]
sha=lambda b:hashlib.sha256(b).hexdigest()
with zipfile.ZipFile(W/'committed-source.zip') as z:
 names={i.filename for i in z.infolist() if not i.is_dir() and i.filename.split('/')[0] in ['Assets','Packages','ProjectSettings']}
 for path in sorted(names):
  f=P/path
  if not f.is_file():changed.append({'path':path,'reason':'missing'});continue
  expected=m['changes'][path]['newSha'] if path in m['changes'] else sha(z.read(path))
  if sha(f.read_bytes())!=expected:changed.append({'path':path,'reason':'bytes_changed','sourceCode':f.suffix in ['.cs','.asmdef','.hlsl','.compute','.shader']})
  count+=1
 for path in m['changes']:
  assert (P/path).is_file() and sha((P/path).read_bytes())==m['changes'][path]['newSha'],path
 for f in (P/'Assets').rglob('*'):
  if f.is_file():
   path=f.relative_to(P).as_posix()
   if path not in names and path not in m['changes']:unexpected.append(path)
sourceChanges=[x for x in changed if x.get('sourceCode') or x['reason']=='missing' or x['path'].startswith('Packages/')]
unexpectedCode=[x for x in unexpected if Path(x).suffix in ['.cs','.asmdef','.hlsl','.compute','.shader']]
result={'baseCommit':m['baseCommit'],'committedProjectFilesChecked':count,'patchFilesChecked':len(m['changes']),'changedTrackedFiles':changed,'unexpectedAssets':unexpected,'sourceAndPackageProvenancePassed':not sourceChanges and not unexpectedCode,'scope':'Checks all committed Unity project files against Git archive plus explicit repair manifest; records Editor auto-generated/updated assets/settings rather than hiding them. No mother compiled assemblies, AssetDatabase or project JIT reused; pinned package source cache copies audited separately.'}
(W/'source-provenance.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps(result,ensure_ascii=False,indent=2))
assert result['sourceAndPackageProvenancePassed']

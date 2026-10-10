from common_p0 import *
import re,sys
sys.stdout.reconfigure(encoding='utf-8')
log=(P0/'editmode-01/Editor.log').read_text(encoding='utf-8',errors='replace')
warnings=sorted(set(re.findall(r'^.*warning CS\d+.*$',log,re.M)))
result={'scope':'C# compiler warning lines from the current full-suite log only; not a claim to cover all Unity/shader/runtime warnings','ownedHelper':[x for x in warnings if 'InteractionRefinementP0.cs' in x],'existingSources':[x for x in warnings if 'InteractionRefinementP0.cs' not in x]}
save(P0/'compiler-warnings.json',result)
print(json.dumps(result,ensure_ascii=False,indent=2),flush=True)
